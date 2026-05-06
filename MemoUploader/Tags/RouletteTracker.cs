using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using MemoUploader.Helpers;


namespace MemoUploader.Tags;

/// <summary>
///     ACT-side equivalent of the Dalamud uploader's RouletteTags. ACT has
///     no game-memory access, so we tap FFXIV_ACT_Plugin's
///     DataSubscription.NetworkReceived and watch for ContentFinderNotifyPop.
///
///     Packet layout (matcha Network/Packet.cs + NetworkMonitor.cs:407+):
///       0..31    : 32-byte header (opcode at +18..19 LE u16)
///       body +2  : u16 QueuedContentRouletteId
///       body +1c : u16 PoppedContent instance id (when roulette=0)
///       total    : 72 bytes
///
///     Cached id is consumed at the next TerritoryChanged and cleared on
///     consume — fire-and-forget. Subsequent CFNotify packets overwrite.
/// </summary>
internal static class RouletteTracker
{
    // ContentFinderNotifyPop opcode. Patched per game version — current
    // CN patch 7.5 = 0x025F. Source: karashiiro/FFXIVOpcodes feed, mirrored
    // in matcha/Cafe.Matcha/Constant/MatchaOpcode.cs.
    //
    // If a patch shifts this and tags stop emitting: flip
    // DiagnoseUnknownShapes to true, accept any roulette in town, find the
    // new "[Roulette] shape len=72" line whose body[+2] != 0.
    private const ushort CFNotifyOpcode      = 0x025F;
    private const int    CFNotifyTotalLength = 72;
    private const int    HeaderLength        = 32;

    private const bool DiagnoseUnknownShapes = false;
    private const int  ShapeLogMaxLen        = 256;
    private const int  ShapeLogCap           = 4096;

    private static readonly object Gate = new();

    private static byte?          cachedRouletteId;
    private static DateTimeOffset cachedAt;

    // 10-minute window covers normal "queue → load screen → enter content"
    // latency with margin. If a roulette tag attempt fires later than this
    // we treat it as stale and skip — better silent miss than mislabel.
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromMinutes(10);

    private static bool hooked;
    private static long packetsSeen;

    private static readonly ConcurrentDictionary<uint, byte> seenShapes = new();

    /// <summary>Hook DataSubscription.NetworkReceived. Safe to retry if parser not ready.</summary>
    public static bool Init()
    {
        if (hooked)
        {
            LogHelper.Info("[Roulette] init: already-hooked");
            return true;
        }

        var parser = ParseHelper.Parser;
        if (parser is null)
        {
            LogHelper.Warning("[Roulette] init: failed reason=parser-null");
            return false;
        }

        try
        {
            parser.DataSubscription.NetworkReceived += OnNetworkReceived;
            hooked = true;
            LogHelper.Info($"[Roulette] init: opcode=0x{CFNotifyOpcode:X4} len={CFNotifyTotalLength} diag={DiagnoseUnknownShapes}");
            return true;
        }
        catch (Exception ex)
        {
            LogHelper.Error("[Roulette] init: failed reason=subscribe-threw", ex);
            return false;
        }
    }

    public static void Uninit()
    {
        if (!hooked) return;
        var parser = ParseHelper.Parser;
        if (parser is null)
        {
            hooked = false;
            return;
        }

        try
        {
            parser.DataSubscription.NetworkReceived -= OnNetworkReceived;
            LogHelper.Info($"[Roulette] uninit: packets={Interlocked.Read(ref packetsSeen)}");
        }
        catch (Exception ex)
        {
            LogHelper.Error("[Roulette] uninit: failed reason=unsubscribe-threw", ex);
        }
        finally
        {
            hooked = false;
        }
    }

    private static void OnNetworkReceived(string connection, long epoch, byte[] message)
    {
        try
        {
            Interlocked.Increment(ref packetsSeen);

            if (message is null || message.Length < HeaderLength + 2)
                return;

            // bytes[18..19] LE u16 = opcode (matches matcha Packet.cs:85)
            var opcode = BitConverter.ToUInt16(message, 18);

            if (DiagnoseUnknownShapes && message.Length <= ShapeLogMaxLen && seenShapes.Count < ShapeLogCap)
            {
                var key = ((uint)opcode << 16) | (uint)message.Length;
                if (seenShapes.TryAdd(key, 0))
                {
                    if (message.Length == CFNotifyTotalLength)
                    {
                        var b2  = BitConverter.ToUInt16(message, HeaderLength + 2);
                        var b1c = BitConverter.ToUInt16(message, HeaderLength + 0x1C);
                        LogHelper.Debug($"[Roulette] shape: opcode=0x{opcode:X4} len={message.Length} body+2={b2} body+1c={b1c}");
                    }
                    else
                    {
                        LogHelper.Debug($"[Roulette] shape: opcode=0x{opcode:X4} len={message.Length}");
                    }
                }
            }

            if (opcode != CFNotifyOpcode)
                return;

            if (message.Length != CFNotifyTotalLength)
            {
                LogHelper.Warning($"[Roulette] cfnotify: skipped reason=length-mismatch opcode=0x{opcode:X4} len={message.Length} want={CFNotifyTotalLength}");
                return;
            }

            var bodyOffset = HeaderLength;
            var rouletteId = BitConverter.ToUInt16(message, bodyOffset + 2);
            var instanceId = BitConverter.ToUInt16(message, bodyOffset + 0x1C);

            if (rouletteId == 0)
            {
                lock (Gate)
                {
                    var prev = cachedRouletteId;
                    cachedRouletteId = null;
                    LogHelper.Info($"[Roulette] cfnotify: kind=direct-entry instance={instanceId} cleared-prev={prev?.ToString() ?? "null"}");
                }
                return;
            }

            var rid = rouletteId > byte.MaxValue ? (byte)0 : (byte)rouletteId;
            if (rid == 0)
            {
                LogHelper.Warning($"[Roulette] cfnotify: skipped reason=byte-overflow id={rouletteId}");
                return;
            }

            lock (Gate)
            {
                cachedRouletteId = rid;
                cachedAt         = DateTimeOffset.UtcNow;
            }

            LogHelper.Info($"[Roulette] cfnotify: id={rid} name={MapName(rid) ?? "unmapped"} instance={instanceId}");
        }
        catch (Exception ex)
        {
            // Swallow: an uncaught throw would silently unhook us from ACT.
            LogHelper.Error("[Roulette] cfnotify: failed reason=handler-threw", ex);
        }
    }

    /// <summary>
    ///     Consume the cached roulette and clear it (fire-and-forget).
    ///     Multi-zone duties only get the tag on the FIRST sub-zone
    ///     TerritoryChanged; subsequent intra-duty zone changes wipe the
    ///     engine's observedTags. Affects only Alliance Roulette →
    ///     Crystal Tower; the other 10 roulette types are single-zone.
    /// </summary>
    public static IReadOnlyList<string>? BuildTags()
    {
        byte?          rid;
        DateTimeOffset seen;
        lock (Gate)
        {
            rid              = cachedRouletteId;
            seen             = cachedAt;
            cachedRouletteId = null; // fire-and-forget: clear on consume
        }

        if (rid is null)
        {
            LogHelper.Info("[Roulette] consume: empty");
            return null;
        }

        var age = DateTimeOffset.UtcNow - seen;
        if (age > StaleThreshold)
        {
            LogHelper.Info($"[Roulette] consume: empty reason=stale id={rid} age={age.TotalSeconds:F1}s threshold={StaleThreshold.TotalSeconds:F0}s");
            return null;
        }

        var name = MapName(rid.Value);
        if (name is null)
        {
            // Pass through as id-N so the rejection surfaces visibly on
            // the server side rather than being silently dropped client-side.
            var placeholder = $"roulette:id-{rid}";
            LogHelper.Warning($"[Roulette] consume: tag={placeholder} reason=unmapped-id id={rid} age={age.TotalSeconds:F1}s");
            return new List<string> { placeholder };
        }

        var tag = "roulette:" + name;
        LogHelper.Info($"[Roulette] consume: tag={tag} age={age.TotalSeconds:F1}s");
        return new List<string> { tag };
    }

    // Mirrors memo-server/service/fight_validate.go:clientTagRegistry.
    private static string? MapName(byte id) => id switch
    {
        1  => "leveling",
        2  => "high-level",
        3  => "msq",
        4  => "guildhests",
        5  => "expert",
        6  => "trials",
        7  => "frontline",
        8  => "level-cap",
        9  => "mentor",
        15 => "alliance",
        17 => "normal-raids",
        _  => null,
    };
}
