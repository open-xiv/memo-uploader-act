using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Advanced_Combat_Tracker;


namespace MemoUploader.Helpers;

internal static class HpHelper
{
    public static double TryGetEnemyHp(EncounterData encounter)
    {
        if (encounter.Items.Count == 0)
            return 1.0;

        var localName = encounter.CharName ?? "";

        var candidates = new List<HpCandidate>();
        foreach (var combatant in encounter.Items.Values)
        {
            if (combatant is null)
                continue;
            if (!string.IsNullOrWhiteSpace(localName) &&
                string.Equals(combatant.Name, localName, StringComparison.OrdinalIgnoreCase))
                continue;

            var tags = combatant.Tags;
            if (tags is null || tags.Count == 0)
                continue;

            if (!TryGetLongTag(tags, out var maxHp, "MaxHP", "Max HP", "maxhp", "HPMax", "MaxHealth", "max_health", "max_hp"))
                continue;

            if (maxHp <= 0)
                continue;

            if (!TryGetLongTag(tags, out var curHp, "HP", "CurrentHP", "Current HP", "CurHP", "curhp", "currenthp", "hp"))
                continue;

            var ratio = Clamp01(curHp / (double)maxHp);
            candidates.Add(new HpCandidate(maxHp, ratio));
        }

        if (candidates.Count == 0)
            return 1.0;

        var best = candidates.OrderByDescending(c => c.MaxHp).FirstOrDefault();
        return best?.Ratio ?? 1.0;
    }

    private static double Clamp01(double value)
        => value switch
        {
            < 0 => 0,
            > 1 => 1,
            _ => value
        };

    private static bool TryGetLongTag(IDictionary<string, object> tags, out long value, params string[] keys)
    {
        value = 0;

        foreach (var key in keys)
        {
            if (TryGetTag(tags, key, out var obj) && obj is not null && TryConvertLong(obj, out value))
                return true;
        }

        return false;
    }

    private static bool TryGetTag(IDictionary<string, object> tags, string key, out object? value)
    {
        value = null;
        if (string.IsNullOrEmpty(key))
            return false;

        if (tags.TryGetValue(key, out value))
            return true;

        foreach (var kv in tags)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }

        return false;
    }

    private static bool TryConvertLong(object obj, out long value)
    {
        value = 0;

        switch (obj)
        {
            case long l:
                value = l;
                return true;
            case int i:
                value = i;
                return true;
            case short s:
                value = s;
                return true;
            case sbyte sb:
                value = sb;
                return true;
            case ulong ul when ul <= long.MaxValue:
                value = (long)ul;
                return true;
            case uint ui:
                value = ui;
                return true;
            case ushort us:
                value = us;
                return true;
            case byte b:
                value = b;
                return true;
            case double d:
                value = (long)d;
                return true;
            case float f:
                value = (long)f;
                return true;
            case decimal m:
                value = (long)m;
                return true;
            case string s:
            {
                var t = s.Trim();
                if (string.IsNullOrEmpty(t))
                    return false;
                t = t.Replace(",", "");

                if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    value = parsed;
                    return true;
                }

                if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedD))
                {
                    value = (long)parsedD;
                    return true;
                }

                return false;
            }
            default:
            {
                try
                {
                    var str = obj.ToString();
                    if (string.IsNullOrWhiteSpace(str))
                        return false;

                    str = str.Trim().Replace(",", "");
                    if (long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        value = parsed;
                        return true;
                    }

                    if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedD))
                    {
                        value = (long)parsedD;
                        return true;
                    }
                }
                catch { return false; }

                return false;
            }
        }
    }


    private sealed class HpCandidate(long maxHp, double ratio)
    {
        public long   MaxHp { get; } = maxHp;
        public double Ratio { get; } = ratio;
    }
}
