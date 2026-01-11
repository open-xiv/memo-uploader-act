using System;
using Advanced_Combat_Tracker;


namespace MemoUploader.Models;

public interface IEvent
{
    string Category { get; }
    string Message  { get; }

    string FormatMessage();
}

public abstract class BaseEvent : IEvent
{
    public virtual string Category => GetType().Name;
    public virtual string Message  => FormatMessage();

    public virtual string FormatMessage() => ToString();
}

public class EventLog(DateTime time, string category, string message)
{
    public DateTime Time     { get; private set; } = time;
    public string   Category { get; private set; } = category;
    public string   Message  { get; private set; } = message;
}

// GENERAL EVENTS
public class TerritoryChanged(ushort zoneId) : BaseEvent
{
    public ushort ZoneId { get; } = zoneId;

    public override string ToString() => $"{ZoneId}";
}

#region ActionEvents

// ACTION EVENTS
public interface IActionEvent : IEvent
{
    uint   DataId   { get; }
    uint   ActionId { get; }
    string Status   { get; }

    bool Match(Trigger trigger);
}

public abstract class BaseActionEvent(uint dataId, uint actionId) : BaseEvent, IActionEvent
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

public class ActionStarted(uint   dataId, uint actionId) : BaseActionEvent(dataId, actionId);
public class ActionCompleted(uint dataId, uint actionId) : BaseActionEvent(dataId, actionId);

#endregion

#region CombatantEvents

// COMBATANT EVENTS
public interface ICombatantEvent : IEvent
{
    uint   DataId { get; }
    string Status { get; }

    bool Match(Trigger trigger);
}

public abstract class BaseCombatantEvent(uint dataId) : BaseEvent, ICombatantEvent
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

public class CombatantSpawned(uint            dataId) : BaseCombatantEvent(dataId);
public class CombatantDestroyed(uint          dataId) : BaseCombatantEvent(dataId);
public class CombatantBecameTargetable(uint   dataId) : BaseCombatantEvent(dataId);
public class CombatantBecameUntargetable(uint dataId) : BaseCombatantEvent(dataId);

#endregion

#region EnemyStateEvents

public class EnemyHpChanged(uint dataId, double? currentHp, double? maxHp) : BaseEvent
{
    public uint    DataId    { get; } = dataId;
    public double? CurrentHp { get; } = currentHp;
    public double? MaxHp     { get; } = maxHp;

    public override string FormatMessage()
        => $"{DataId} - HP: {CurrentHp}/{MaxHp}";
}

#endregion

#region StatusEvents

// STATUS EVENTS
public interface IStatusEvent : IEvent
{
    uint   EntityId { get; }
    uint   StatusId { get; }
    string Status   { get; }

    bool Match(Trigger trigger);
}

public abstract class BaseStatusEvent(uint entityId, uint statusId) : BaseEvent, IStatusEvent
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

public class StatusApplied(uint entityId, uint statusId) : BaseStatusEvent(entityId, statusId);
public class StatusRemoved(uint entityId, uint statusId) : BaseStatusEvent(entityId, statusId);

#endregion

#region DutyEvents

// DUTY EVENTS
public interface IDutyEvent : IEvent { }

public abstract class BaseDutyEvent : BaseEvent, IDutyEvent
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

public class DutyStarted : BaseDutyEvent;
public class DutyCompleted : BaseDutyEvent;
public class DutyWiped : BaseDutyEvent;

public class DutyEnd(EncounterData encounter) : BaseEvent
{
    public EncounterData Encounter { get; } = encounter;
}

#endregion

#region ConditionEvents

// CONDITION EVENTS
public interface IConditionEvent : IEvent { }

public abstract class BaseConditionEvent : BaseEvent, IConditionEvent
{
    public override string FormatMessage()
        => this switch
        {
            CombatOptIn => "CombatOptIn",
            CombatOptOut => "CombatOptOut",
            _ => "UnknownConditionEvent"
        };
}

public class CombatOptIn : BaseConditionEvent;
public class CombatOptOut : BaseConditionEvent;

#endregion

#region FightEvents

// FIGHT EVENTS
public class PlayerDied(uint entityId) : BaseEvent
{
    public uint EntityId { get; } = entityId;

    public override string FormatMessage()
        => $"{EntityId}";
}

#endregion
