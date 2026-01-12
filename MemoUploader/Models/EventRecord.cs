using System;
using Advanced_Combat_Tracker;


namespace MemoUploader.Models;

internal interface IEvent
{
    string Category { get; }
    string Message  { get; }

    string FormatMessage();
}

internal abstract class BaseEvent : IEvent
{
    public virtual string Category => GetType().Name;
    public virtual string Message  => FormatMessage();

    public virtual string FormatMessage() => ToString();
}

internal class EventLog(DateTime time, string category, string message)
{
    public DateTime Time     { get; private set; } = time;
    public string   Category { get; private set; } = category;
    public string   Message  { get; private set; } = message;
}

// GENERAL EVENTS
internal class TerritoryChanged(ushort zoneId) : BaseEvent
{
    public ushort ZoneId { get; } = zoneId;

    public override string ToString() => $"{ZoneId}";
}

#region ActionEvents

// ACTION EVENTS
internal interface IActionEvent : IEvent
{
    uint   DataId   { get; }
    uint   ActionId { get; }
    string Status   { get; }

    bool Match(Trigger trigger);
}

internal abstract class BaseActionEvent(uint dataId, uint actionId) : BaseEvent, IActionEvent
{
    public uint DataId   { get; } = dataId;
    public uint ActionId { get; } = actionId;

    public string Status => this switch
    {
        ActionStarted => "START",
        ActionCompleted => "COMPLETE",
        _ => "UNKNOWN"
    };

    public bool Match(Trigger trigger)
    {
        if (trigger.Type != "ACTION_EVENT")
            return false;

        var actionMatch = trigger.ActionId.HasValue && trigger.ActionId.Value == ActionId;
        var statusMatch = trigger.Status == Status;

        return actionMatch && statusMatch;
    }

    public override string FormatMessage()
        => $"{DataId} - {ActionId}";
}

internal class ActionStarted(uint   dataId, uint actionId) : BaseActionEvent(dataId, actionId);
internal class ActionCompleted(uint dataId, uint actionId) : BaseActionEvent(dataId, actionId);

#endregion

#region CombatantEvents

// COMBATANT EVENTS
internal interface ICombatantEvent : IEvent
{
    uint   DataId { get; }
    string Status { get; }

    bool Match(Trigger trigger);
}

internal abstract class BaseCombatantEvent(uint dataId) : BaseEvent, ICombatantEvent
{
    public uint DataId { get; } = dataId;

    public string Status => this switch
    {
        CombatantSpawned => "SPAWN",
        CombatantDestroyed => "DESTROY",
        CombatantBecameTargetable => "TARGETABLE",
        CombatantBecameUntargetable => "UNTARGETABLE",
        _ => "UNKNOWN"
    };

    public bool Match(Trigger trigger)
    {
        if (trigger.Type != "COMBATANT_EVENT")
            return false;

        var combatantMatch = trigger.NpcId.HasValue && trigger.NpcId.Value == DataId;
        var statusMatch    = trigger.Status == Status;

        return combatantMatch && statusMatch;
    }

    public override string FormatMessage()
        => $"{DataId} - {Status}";
}

internal class CombatantSpawned(uint            dataId) : BaseCombatantEvent(dataId);
internal class CombatantDestroyed(uint          dataId) : BaseCombatantEvent(dataId);
internal class CombatantBecameTargetable(uint   dataId) : BaseCombatantEvent(dataId);
internal class CombatantBecameUntargetable(uint dataId) : BaseCombatantEvent(dataId);

#endregion

#region StatusEvents

// STATUS EVENTS
internal interface IStatusEvent : IEvent
{
    uint   EntityId { get; }
    uint   StatusId { get; }
    string Status   { get; }

    bool Match(Trigger trigger);
}

internal abstract class BaseStatusEvent(uint entityId, uint statusId) : BaseEvent, IStatusEvent
{
    public uint EntityId { get; } = entityId;
    public uint StatusId { get; } = statusId;

    public string Status => this switch
    {
        StatusApplied => "APPLIED",
        StatusRemoved => "REMOVED",
        _ => "UNKNOWN"
    };

    public bool Match(Trigger trigger)
    {
        if (trigger.Type != "STATUS_EVENT")
            return false;

        var staMatch    = trigger.StatusId.HasValue && trigger.StatusId.Value == StatusId;
        var statusMatch = trigger.Status == Status;

        return staMatch && statusMatch;
    }

    public override string FormatMessage()
        => $"{EntityId} - {StatusId}";
}

internal class StatusApplied(uint entityId, uint statusId) : BaseStatusEvent(entityId, statusId);
internal class StatusRemoved(uint entityId, uint statusId) : BaseStatusEvent(entityId, statusId);

#endregion

#region DutyEvents

// DUTY EVENTS
internal interface IDutyEvent : IEvent { }

internal abstract class BaseDutyEvent : BaseEvent, IDutyEvent
{
    public override string FormatMessage()
        => this switch
        {
            DutyStarted => "DutyStarted",
            DutyCompleted => "DutyCompleted",
            DutyWiped => "DutyWiped",
            _ => "UnknownDutyEvent"
        };
}

internal class DutyStarted : BaseDutyEvent;
internal class DutyCompleted : BaseDutyEvent;
internal class DutyWiped : BaseDutyEvent;

internal class DutyEnd(EncounterData encounter) : BaseEvent
{
    public EncounterData Encounter { get; } = encounter;
}

#endregion

#region ConditionEvents

// CONDITION EVENTS
internal interface IConditionEvent : IEvent { }

internal abstract class BaseConditionEvent : BaseEvent, IConditionEvent
{
    public override string FormatMessage()
        => this switch
        {
            CombatOptIn => "CombatOptIn",
            CombatOptOut => "CombatOptOut",
            _ => "UnknownConditionEvent"
        };
}

internal class CombatOptIn : BaseConditionEvent;
internal class CombatOptOut : BaseConditionEvent;

#endregion

#region FightEvents

// FIGHT EVENTS
internal class PlayerDied(uint entityId) : BaseEvent
{
    private uint EntityId { get; } = entityId;

    public override string FormatMessage()
        => $"{EntityId}";
}

#endregion
