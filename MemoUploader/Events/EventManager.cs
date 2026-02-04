using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using Advanced_Combat_Tracker;
using FFXIV_ACT_Plugin.Common.Models;
using MemoEngine;
using MemoUploader.Helpers;
using PlayerPayload = MemoEngine.Models.PlayerPayload;


namespace MemoUploader.Events;

/// <summary>
///     ACT 日志行解析器
///     使用 OnLogLineRead 事件获取 ACT 日志格式（十六进制）
///     ACT 日志格式: [timestamp] TypeName HexCode:field1:field2:...
///     日志类型参考:
///     00: ChatLog - 聊天/系统消息
///     03: AddCombatant - 添加战斗对象
///     04: RemoveCombatant - 移除战斗对象
///     14: StartsCasting - 开始咏唱
///     15: ActionEffect - 技能效果（单体）
///     16: AOEActionEffect - 技能效果（群体）
///     19: Death - 死亡
///     1A: StatusAdd - 获得状态
///     1E: StatusRemove - 失去状态
///     21: Director - 副本演出（团灭/胜利）
///     22: NameToggle - 切换可选中
///     104: InCombat - 进战状态
/// </summary>
internal class EventManager
{
    private readonly Timer updateHpTimer;

    public EventManager()
    {
        updateHpTimer                     =  new Timer(200);
        updateHpTimer.Elapsed             += (_, _) => UpdateHp();
        updateHpTimer.AutoReset           =  true;
        updateHpTimer.SynchronizingObject =  ActGlobals.oFormActMain;
    }

    #region Hook

    public void Init()
    {
        ActGlobals.oFormActMain.BeforeLogLineRead += OnBeforeLogLineRead;
        updateHpTimer.Start();
    }

    public void Uninit()
    {
        ActGlobals.oFormActMain.BeforeLogLineRead -= OnBeforeLogLineRead;
        updateHpTimer.Stop();
        updateHpTimer.Dispose();
    }

    #endregion

    #region Parsing

    private static void OnBeforeLogLineRead(bool isImport, LogLineEventArgs logInfo)
    {
        if (isImport)
            return;
        ParseLogLine(logInfo.logLine);
    }

    /// <summary>
    ///     解析 ACT 日志行
    ///     格式: [timestamp] TypeName HexCode:field1:field2:...
    /// </summary>
    private static void ParseLogLine(string logLine)
    {
        if (string.IsNullOrEmpty(logLine))
            return;

        try
        {
            // ACT 日志格式: [12:34:56.789] TypeName HexCode:field1:field2:...
            // 找到第一个冒号后的内容（跳过时间戳中的冒号）
            var closeBracket = logLine.IndexOf(']');
            if (closeBracket < 0)
                return;

            // 提取类型名和数据部分
            var afterTimestamp = logLine.Substring(closeBracket + 1).TrimStart();

            // 找到空格分隔的类型名
            var spaceIndex = afterTimestamp.IndexOf(' ');
            if (spaceIndex < 0)
                return;

            var dataSection = afterTimestamp.Substring(spaceIndex + 1);

            // 按冒号分割数据
            var parts = dataSection.Split(':');
            if (parts.Length < 1)
                return;

            // 第一个字段是十六进制日志类型
            var logType = parts[0].ToUpperInvariant();

            switch (logType)
            {
                // 系统消息
                case "00":
                    ParseChatLog(parts);
                    break;

                // 区域变更
                case "01":
                    ParseZoneChange(parts);
                    break;

                // 添加战斗对象
                case "03":
                    ParseAddCombatant(parts);
                    break;

                // 移除战斗对象
                case "04":
                    ParseRemoveCombatant(parts);
                    break;

                // 开始咏唱
                case "14":
                    ParseStartCasting(parts);
                    break;

                // 技能效果-单体
                case "15":
                    ParseActionEffect(parts);
                    break;

                // 技能效果-群体
                case "16":
                    ParseAOEActionEffect(parts);
                    break;

                // 死亡
                case "19":
                    ParseDeath(parts);
                    break;

                // 获得状态
                case "1A":
                    ParseStatusAdd(parts);
                    break;

                // 失去状态
                case "1E":
                    ParseStatusRemove(parts);
                    break;

                // 副本演出-团灭/胜利
                case "21":
                    ParseDirector(parts);
                    break;

                // 切换可选中
                case "22":
                    ParseTargetableUpdate(parts);
                    break;

                // 进战状态
                case "104":
                    ParseInCombat(parts);
                    break;
            }
        }
        catch (Exception)
        {
            // ignored
        }
    }

    /// <summary>
    ///     解析聊天/系统消息 - 检测副本通关/团灭
    ///     ACT格式: 00:code:name:message
    /// </summary>
    private static void ParseChatLog(string[] parts)
    {
        if (parts.Length < 4)
            return;

        var message = parts[3];

        // 检测副本完成
        if (message.Contains("has been completed") ||
            message.Contains("Duty Complete") ||
            message.Contains("任务完成") ||
            message.Contains("副本完成") ||
            message.Contains("攻略完了"))
            Event.General.RaiseDutyCompleted(DateTimeOffset.UtcNow);
        // 检测团灭
        else if (message.Contains("has ended") ||
                 message.Contains("The party has been defeated") ||
                 message.Contains("全灭") ||
                 message.Contains("团灭"))
            Event.General.RaiseDutyWiped(DateTimeOffset.UtcNow);
    }

    private static void ParseZoneChange(string[] parts)
    {
        if (parts.Length < 2)
            return;

        var zoneId = (ushort)LogParser.TryParseHex(parts[1]);
        if (zoneId <= 0)
            return;

        Event.General.RaiseTerritoryChanged(DateTimeOffset.UtcNow, zoneId);
    }

    #endregion

    #region Combatant Events

    /// <summary>
    ///     解析添加战斗对象
    ///     ACT格式: 03:id:name:job:level:ownerId:worldId:world:npcNameId:npcBaseId:currentHp:hp:...
    /// </summary>
    private static void ParseAddCombatant(string[] parts)
    {
        if (parts.Length < 12)
            return;

        Event.Combatant.RaiseSpawned(DateTimeOffset.UtcNow, LogParser.TryParseHex(parts[1]));
    }

    /// <summary>
    ///     解析移除战斗对象
    ///     ACT格式: 04:id:name:job:level:...
    /// </summary>
    private static void ParseRemoveCombatant(string[] parts)
    {
        if (parts.Length < 3)
            return;

        Event.Combatant.RaiseDestroyed(DateTimeOffset.UtcNow, LogParser.TryParseHex(parts[1]));
    }

    /// <summary>
    ///     解析切换可选中 - BOSS转阶段关键
    ///     ACT格式: 22:id:name:targetId:targetName:toggle
    ///     toggle: 01=可选中, 00=不可选中
    /// </summary>
    private static void ParseTargetableUpdate(string[] parts)
    {
        if (parts.Length < 6)
            return;

        var targetable = parts[5] == "01";
        if (targetable)
            Event.Combatant.RaiseBecameTargetable(DateTimeOffset.UtcNow, LogParser.TryParseHex(parts[1]));
        else
            Event.Combatant.RaiseBecameUntargetable(DateTimeOffset.UtcNow, LogParser.TryParseHex(parts[1]));
    }

    #endregion

    #region Action Events

    /// <summary>
    ///     解析开始咏唱
    ///     ACT格式: 14:sourceId:sourceName:actionId:actionName:targetId:targetName:castTime:...
    /// </summary>
    private static void ParseStartCasting(string[] parts)
    {
        if (parts.Length < 7)
            return;

        Event.Action.RaiseStarted(DateTimeOffset.UtcNow, LogParser.TryParseHex(parts[1]), LogParser.TryParseHex(parts[3]));
    }

    /// <summary>
    ///     解析技能效果（单体）
    ///     ACT格式: 15:sourceId:sourceName:actionId:actionName:targetId:targetName:flags:damage:...
    /// </summary>
    private static void ParseActionEffect(string[] parts)
    {
        if (parts.Length < 7)
            return;

        Event.Action.RaiseCompleted(DateTimeOffset.UtcNow, LogParser.TryParseHex(parts[1]), LogParser.TryParseHex(parts[3]));
    }

    /// <summary>
    ///     解析技能效果（AOE）
    ///     ACT格式: 16:sourceId:sourceName:actionId:actionName:targetId:targetName:flags:damage:...
    /// </summary>
    private static void ParseAOEActionEffect(string[] parts) // 格式与 15 相同
        => ParseActionEffect(parts);

    #endregion

    #region Fight Events

    /// <summary>
    ///     解析死亡
    ///     ACT格式: 19:targetId:targetName:sourceId:sourceName
    /// </summary>
    private static void ParseDeath(string[] parts)
    {
        if (parts.Length < 5)
            return;

        Event.General.RaisePlayerDied(DateTimeOffset.UtcNow, LogParser.TryParseHex(parts[1]));
    }

    private static void UpdateHp()
    {
        if (Context.EnemyDataId == 0)
            return;

        var repo  = ParseHelper.Parser?.DataRepository;
        var enemy = repo?.GetCombatantList().FirstOrDefault(c => c.BNpcID == Context.EnemyDataId);
        if (enemy is null)
            return;

        Event.Combatant.RaiseHpUpdated(DateTimeOffset.UtcNow, enemy.BNpcID, enemy.CurrentHP, enemy.MaxHP);
    }

    #endregion

    #region Status Events

    /// <summary>
    ///     解析获得状态
    ///     ACT格式: 1A:statusId:statusName:duration:sourceId:sourceName:targetId:targetName:stacks:targetMaxHp:sourceMaxHp
    /// </summary>
    private static void ParseStatusAdd(string[] parts)
    {
        if (parts.Length < 9)
            return;

        Event.Status.RaiseApplied(DateTimeOffset.UtcNow, LogParser.TryParseHex(parts[6]), LogParser.TryParseHex(parts[1]));
    }

    /// <summary>
    ///     解析失去状态
    ///     ACT格式: 1E:statusId:statusName:?:sourceId:sourceName:targetId:targetName:stacks
    /// </summary>
    private static void ParseStatusRemove(string[] parts)
    {
        if (parts.Length < 9)
            return;

        Event.Status.RaiseRemoved(DateTimeOffset.UtcNow, LogParser.TryParseHex(parts[6]), LogParser.TryParseHex(parts[1]));
    }

    #endregion

    #region Duty Events

    /// <summary>
    ///     解析副本演出 - 检测团灭/胜利
    ///     ACT格式: 21:instance:command:data0:data1:data2:data3
    ///     已知 command:
    ///     40000001 = 初次进入副本
    ///     40000003 = 副本胜利
    ///     40000005 = 团灭黑屏
    ///     40000006 = "重新挑战！"
    ///     40000013 = 团灭日志1
    ///     4000000F = 团灭日志2
    ///     40000011 = 团灭日志3 (战斗数据已清除)
    /// </summary>
    private static void ParseDirector(string[] parts)
    {
        if (parts.Length < 3)
            return;

        var command = parts[2];

        switch (command)
        {
            // 副本胜利
            case "40000003":
            case "40000002": // 多变迷宫
                Event.General.RaiseDutyCompleted(DateTimeOffset.UtcNow);
                break;

            // 团灭 - 使用最早的团灭黑屏信号
            case "40000005":
                Event.General.RaiseDutyWiped(DateTimeOffset.UtcNow);
                break;
        }
    }

    #endregion

    #region Condition Events

    /// <summary>
    ///     解析进战状态 - 检测战斗开始/结束
    ///     ACT格式: 104:inACTCombat:inGameCombat:isACTChanged:isGameChanged
    ///     进战: inGameCombat=1 且 isGameChanged=1
    ///     脱战: inGameCombat=0 且 isGameChanged=1
    /// </summary>
    private static void ParseInCombat(string[] parts)
    {
        if (parts.Length < 5)
            return;

        var inGameCombat  = parts[2];
        var isGameChanged = parts[4];

        if (isGameChanged != "1")
            return;

        switch (inGameCombat)
        {
            case "1":
                var partySnapshots = GetPartySnapshots();
                Event.General.RaiseCombatOptIn(DateTimeOffset.UtcNow, partySnapshots);
                LogHelper.Info("[Lifecycle]: Enter Combat");
                foreach (var kv in partySnapshots)
                    LogHelper.Info($"[Party List]: {kv.Value.Name}@{kv.Value.Server} (id: {kv.Key}) <job: {kv.Value.JobId}>");
                break;
            case "0":
                Event.General.RaiseCombatOptOut(DateTimeOffset.UtcNow);
                break;
        }
    }

    private static Dictionary<uint, PlayerPayload> GetPartySnapshots()
    {
        var repo = ParseHelper.Parser?.DataRepository;
        if (repo is null)
            return [];

        return repo.GetCombatantList()
                   .Where(c => c.PartyType == PartyType.Party)
                   .ToDictionary(p => p.ID,
                                 p => new PlayerPayload
                                 {
                                     Name       = p.Name,
                                     Server     = MapHelper.ServerEnToZh(p.WorldName),
                                     JobId      = (uint)Math.Max(0, p.Job),
                                     Level      = (uint)Math.Max(0, p.Level),
                                     DeathCount = 0
                                 });
    }

    #endregion
}
