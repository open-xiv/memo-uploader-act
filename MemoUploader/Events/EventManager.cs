using System;
using System.Collections.Generic;
using Advanced_Combat_Tracker;
using MemoUploader.Helpers;
using MemoUploader.Models;


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
public class EventManager
{
    public event Action<IEvent>? OnEvent;

    private void RaiseEvent(IEvent e) =>
        OnEvent?.Invoke(e);

    #region Hook

    public void Init()
    {
        ActGlobals.oFormActMain.BeforeLogLineRead += OnBeforeLogLineRead;
        ActGlobals.oFormActMain.OnCombatStart     += OnCombatStart;
        ActGlobals.oFormActMain.OnCombatEnd       += OnCombatEnd;
    }

    public void Uninit()
    {
        ActGlobals.oFormActMain.BeforeLogLineRead -= OnBeforeLogLineRead;
        ActGlobals.oFormActMain.OnCombatStart     -= OnCombatStart;
        ActGlobals.oFormActMain.OnCombatEnd       -= OnCombatEnd;
    }

    private void OnCombatStart(bool isImport, CombatToggleEventArgs encounterInfo)
    {
        if (isImport)
            return;

        RaiseEvent(new DutyStarted());
    }

    private void OnCombatEnd(bool isImport, CombatToggleEventArgs encounterInfo)
    {
        if (isImport)
            return;

        RaiseEvent(new DutyEnd(encounterInfo.encounter));
    }

    #endregion

    #region Cache

    private ushort currentZoneId;

    #endregion

    #region Parsing

    private void OnBeforeLogLineRead(bool isImport, LogLineEventArgs logInfo)
    {
        if (isImport)
            return;
        ParseLogLine(logInfo.logLine);
    }

    /// <summary>
    ///     解析 ACT 日志行
    ///     格式: [timestamp] TypeName HexCode:field1:field2:...
    /// </summary>
    public void ParseLogLine(string logLine)
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

                // 小队成员变更
                case "0B":
                    ParsePartyChange(parts);
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
    private void ParseChatLog(string[] parts)
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
            RaiseEvent(new DutyCompleted());
        // 检测团灭
        else if (message.Contains("has ended") ||
                 message.Contains("The party has been defeated") ||
                 message.Contains("全灭") ||
                 message.Contains("团灭"))
            RaiseEvent(new DutyWiped());
    }

    private void ParseZoneChange(string[] parts)
    {
        if (parts.Length < 2)
            return;

        var oldZoneId = currentZoneId;
        currentZoneId = (ushort)LogParser.TryParseHex(parts[1]);
        if (currentZoneId == oldZoneId || currentZoneId <= 0)
            return;

        PluginContext.PartyProvider.PlayerCache    = [];
        PluginContext.PartyProvider.PartyPlayerIds = [];
        RaiseEvent(new TerritoryChanged(currentZoneId));
    }

    /// <summary>
    ///     解析小队成员变更
    ///     ACT格式: 0B:[partyCount]:[id0]:[id1]:[id2]:[id3]:[id4]:...
    /// </summary>
    /// <param name="parts"></param>
    private void ParsePartyChange(string[] parts)
    {
        if (parts.Length < 3)
            return;

        var partyCount = LogParser.TryParseHex(parts[1]);
        var partyIds   = new HashSet<uint>();
        for (var i = 0; i < partyCount; i++)
        {
            var index = 2 + i;
            if (index >= parts.Length)
                break;

            var memberId = LogParser.TryParseHex(parts[index]);
            if (memberId != 0)
                partyIds.Add(memberId);
        }

        PluginContext.PartyProvider.PartyPlayerIds = partyIds;
    }

    #endregion

    #region Combatant Events

    /// <summary>
    ///     解析添加战斗对象
    ///     ACT格式: 03:id:name:job:level:ownerId:worldId:world:npcNameId:npcBaseId:currentHp:hp:...
    /// </summary>
    private void ParseAddCombatant(string[] parts)
    {
        if (parts.Length < 12)
            return;

        var worldId = LogParser.TryParseHex(parts[6]);
        if (worldId != 0 && PluginContext.PartyProvider.PlayerCache.Count < 100)
        {
            var entityId = LogParser.TryParseHex(parts[1]);
            var player = new PlayerSnapshot
            {
                EntityId = entityId,
                Name     = parts[2],
                Server   = MapHelper.ServerEnToZh(parts[7]),
                JobId    = LogParser.TryParseHex(parts[3]),
                Level    = LogParser.TryParseHex(parts[4])
            };
            PluginContext.PartyProvider.PlayerCache[entityId] = player;
        }

        RaiseEvent(new CombatantSpawned(LogParser.TryParseHex(parts[1])));
    }

    /// <summary>
    ///     解析移除战斗对象
    ///     ACT格式: 04:id:name:job:level:...
    /// </summary>
    private void ParseRemoveCombatant(string[] parts)
    {
        if (parts.Length < 3)
            return;

        RaiseEvent(new CombatantDestroyed(LogParser.TryParseHex(parts[1])));
    }

    /// <summary>
    ///     解析切换可选中 - BOSS转阶段关键
    ///     ACT格式: 22:id:name:targetId:targetName:toggle
    ///     toggle: 01=可选中, 00=不可选中
    /// </summary>
    private void ParseTargetableUpdate(string[] parts)
    {
        if (parts.Length < 6)
            return;

        var targetable = parts[5] == "01";
        if (targetable)
            RaiseEvent(new CombatantBecameTargetable(LogParser.TryParseHex(parts[1])));
        else
            RaiseEvent(new CombatantBecameUntargetable(LogParser.TryParseHex(parts[1])));
    }

    #endregion

    #region Action Events

    /// <summary>
    ///     解析开始咏唱
    ///     ACT格式: 14:sourceId:sourceName:actionId:actionName:targetId:targetName:castTime:...
    /// </summary>
    private void ParseStartCasting(string[] parts)
    {
        if (parts.Length < 7)
            return;

        RaiseEvent(new ActionStarted(LogParser.TryParseHex(parts[1]), LogParser.TryParseHex(parts[3])));
    }

    /// <summary>
    ///     解析技能效果（单体）
    ///     ACT格式: 15:sourceId:sourceName:actionId:actionName:targetId:targetName:flags:damage:...
    /// </summary>
    private void ParseActionEffect(string[] parts)
    {
        if (parts.Length < 7)
            return;

        RaiseEvent(new ActionCompleted(LogParser.TryParseHex(parts[1]), LogParser.TryParseHex(parts[3])));
    }

    /// <summary>
    ///     解析技能效果（AOE）
    ///     ACT格式: 16:sourceId:sourceName:actionId:actionName:targetId:targetName:flags:damage:...
    /// </summary>
    private void ParseAOEActionEffect(string[] parts) // 格式与 15 相同
        => ParseActionEffect(parts);

    #endregion

    #region Fight Events

    /// <summary>
    ///     解析死亡
    ///     ACT格式: 19:targetId:targetName:sourceId:sourceName
    /// </summary>
    private void ParseDeath(string[] parts)
    {
        if (parts.Length < 5)
            return;

        RaiseEvent(new PlayerDied(LogParser.TryParseHex(parts[1])));
    }

    #endregion

    #region Status Events

    /// <summary>
    ///     解析获得状态
    ///     ACT格式: 1A:statusId:statusName:duration:sourceId:sourceName:targetId:targetName:stacks:targetMaxHp:sourceMaxHp
    /// </summary>
    private void ParseStatusAdd(string[] parts)
    {
        if (parts.Length < 9)
            return;

        RaiseEvent(new StatusApplied(LogParser.TryParseHex(parts[6]), LogParser.TryParseHex(parts[1])));
    }

    /// <summary>
    ///     解析失去状态
    ///     ACT格式: 1E:statusId:statusName:?:sourceId:sourceName:targetId:targetName:stacks
    /// </summary>
    private void ParseStatusRemove(string[] parts)
    {
        if (parts.Length < 9)
            return;

        RaiseEvent(new StatusRemoved(LogParser.TryParseHex(parts[6]), LogParser.TryParseHex(parts[1])));
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
    private void ParseDirector(string[] parts)
    {
        if (parts.Length < 3)
            return;

        var command = parts[2];

        switch (command)
        {
            // 副本胜利
            case "40000003":
            case "40000002": // 多变迷宫
                RaiseEvent(new DutyCompleted());
                break;

            // 团灭 - 使用最早的团灭黑屏信号
            case "40000005":
                RaiseEvent(new DutyWiped());
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
    private void ParseInCombat(string[] parts)
    {
        if (parts.Length < 5)
            return;

        var inGameCombat  = parts[2];
        var isGameChanged = parts[4];

        // 只处理游戏状态变化
        if (isGameChanged != "1")
            return;

        switch (inGameCombat)
        {
            case "1":
                RaiseEvent(new CombatOptIn());
                break;
            case "0":
                RaiseEvent(new CombatOptOut());
                break;
        }
    }

    #endregion
}
