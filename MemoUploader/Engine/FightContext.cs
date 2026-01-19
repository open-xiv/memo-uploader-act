using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Advanced_Combat_Tracker;
using FFXIV_ACT_Plugin.Common.Models;
using MemoUploader.Api;
using MemoUploader.Helpers;
using MemoUploader.Models;
using Action = MemoUploader.Models.Action;


namespace MemoUploader.Engine;

internal class FightContext
{
    // duty config
    public readonly DutyConfig DutyConfig;

    #region Payload

    // progress
    private bool? isClear;
    private int   phaseIndex;
    private int   subphaseIndex;

    #endregion

    #region DutyState

    // variables
    private ConcurrentDictionary<string, object?> variables = [];

    // listener
    private readonly ListenerManager listenerManager = new();

    // checkpoints
    private ConcurrentBag<string> completedCheckpoints = [];

    #endregion

    #region Windows

    private void UpdateContext()
    {
        var phase = DutyConfig.Timeline.Phases[Math.Max(phaseIndex, 0)];
        PluginContext.CurrentPhase = phase.Name;
        PluginContext.CurrentSubphase = subphaseIndex >= 0 && subphaseIndex < phase.CheckpointNames.Count
                                            ? phase.CheckpointNames[subphaseIndex]
                                            : string.Empty;
        PluginContext.Checkpoints   = phase.CheckpointNames.Select(name => (name, completedCheckpoints.Contains(name))).ToArray();
        PluginContext.VariableStats = variables;
    }

    #endregion

    #region Lifecycle

    public FightContext(DutyConfig dutyConfig)
    {
        // props
        DutyConfig = dutyConfig;

        // reset state
        ResetState();
    }

    #endregion

    #region EventProcess

    public void ProcessEvent(IEvent e)
    {
        // lifecycle related events
        LifecycleEvent(e);

        if (PluginContext.Lifecycle is not EngineState.InProgress)
            return;

        // listeners
        var relatedListener = listenerManager.FetchListeners(e);
        foreach (var listener in relatedListener)
        {
            if (CheckTrigger(listener.Trigger, e))
                EmitMechanic(listener.Mechanic);
        }
    }

    private void LifecycleEvent(IEvent e)
    {
        switch (e)
        {
            case DutyStarted:
                if (PluginContext.Lifecycle is EngineState.Completed or EngineState.Ready)
                {
                    LogHelper.Info($"Duty [{DutyConfig.ZoneId}] started: initializing fight context");
                    ResetState();
                    PluginContext.Lifecycle = EngineState.InProgress;
                }
                break;

            case DutyWiped:
                LogHelper.Info($"Duty [{DutyConfig.ZoneId}] wiped: marking as not clear");
                isClear = false;
                break;

            case DutyCompleted:
                LogHelper.Info($"Duty [{DutyConfig.ZoneId}] completed: marking as clear");
                isClear = true;
                break;

            case DutyEnd end:
                LogHelper.Info($"Duty [{DutyConfig.ZoneId}] ended: uploading fight record");
                PluginContext.Lifecycle = EngineState.Completed;
                CompletedSnap(end);
                break;
        }
    }

    #endregion

    #region Snapshot

    private void CompletedSnap(DutyEnd e)
    {
        var encounter = e.Encounter;

        // time
        var startTime  = MapHelper.TimeToUtc(encounter.StartTime);
        var endTime    = MapHelper.TimeToUtc(encounter.EndTime);
        var durationNs = (endTime - startTime).Ticks * 100;
        durationNs = Math.Max(durationNs, 1);

        // enemy hp
        var enemyHp = HpHelper.TryGetEnemyHp(encounter);
        var repo    = ParseHelper.Parser?.DataRepository;
        var enemy   = repo?.GetCombatantList().First(c => c.BNpcID == PluginContext.EnemyDataId);
        if (enemy is not null)
            enemyHp = enemy.CurrentHP / (double)enemy.MaxHP;

        // clear
        var clear = isClear ?? enemyHp <= 1e-3;

        // progress
        var progress = new FightProgressPayload
        {
            PhaseId    = (uint)Math.Max(0, phaseIndex),
            SubphaseId = (uint)Math.Max(0, subphaseIndex),
            EnemyId    = PluginContext.EnemyDataId,
            EnemyHp    = enemyHp
        };

        // payload
        var payload = new FightRecordPayload
        {
            StartTime = startTime,
            Duration  = durationNs,
            ZoneId    = DutyConfig.ZoneId,
            Players   = GetPartyPayload(encounter),
            IsClear   = clear,
            Progress  = progress
        };

        // upload
        _ = Task.Run(async () => await ApiClient.UploadFight(payload));
    }

    private static List<PlayerPayload> GetPartyPayload(EncounterData encounter)
    {
        var repo = ParseHelper.Parser?.DataRepository;
        if (repo is null)
        {
            LogHelper.Warning("Fetch party payload failed: parser repository is null");
            return [];
        }

        var players         = new List<PlayerPayload>();
        var partyMembers    = repo.GetCombatantList().Where(c => c.PartyType == PartyType.Party).ToList();
        var hasAssumedLocal = false;
        foreach (var member in partyMembers)
        {
            // death count
            var combatant = encounter.Items.Values.FirstOrDefault(x => x.Name == member.Name);
            switch (combatant)
            {
                case null when !hasAssumedLocal:
                    combatant       = encounter.Items.Values.FirstOrDefault(x => x.Name == "YOU");
                    hasAssumedLocal = true;
                    break;
                case null:
                    LogHelper.Warning($"Fetch party payload: combatant data not found for {member.Name}");
                    continue;
            }

            // basic info
            players.Add(new PlayerPayload
            {
                Name       = member.Name,
                Server     = MapHelper.ServerEnToZh(member.WorldName),
                JobId      = (uint)Math.Max(0, member.Job),
                Level      = (uint)Math.Max(0, member.Level),
                DeathCount = (uint)Math.Max(0, combatant?.Deaths ?? 0)
            });
        }
        return players;
    }

    #endregion

    #region StateMachine

    private void ResetState()
    {
        // lifecycle
        PluginContext.Lifecycle = EngineState.Ready;
        isClear                 = false;

        // progress
        phaseIndex    = 0;
        subphaseIndex = -1;

        // enemy
        PluginContext.EnemyDataId = 0;

        // clear listeners & checkpoints
        listenerManager.Clear();
        completedCheckpoints = [];

        // variables
        variables = [];
        foreach (var vars in DutyConfig.Variables)
            variables[vars.Name] = vars.Initial;

        // enter start phase
        EnterPhase(0);

        // update context
        UpdateContext();
    }

    private void EnterPhase(int phaseId)
    {
        // phase transition
        var phase = DutyConfig.Timeline.Phases[phaseId];
        phaseIndex    = phaseId;
        subphaseIndex = -1;

        // enemy
        PluginContext.EnemyDataId = phase.TargetId;

        // clear triggers
        listenerManager.Clear();

        // reset checkpoints
        completedCheckpoints = [];

        // mechanics
        // from checkpoints
        var mechanics = new HashSet<string>(phase.CheckpointNames);
        // from transitions
        foreach (var transition in phase.Transitions)
        {
            foreach (var condition in transition.Conditions)
            {
                if (condition.Type != "MECHANIC_TRIGGERED")
                    continue;
                mechanics.Add(condition.MechanicName);
            }
        }

        // register listeners
        foreach (var mechanic in DutyConfig.Mechanics.Where(m => mechanics.Contains(m.Name)))
            listenerManager.Register(new ListenerState(mechanic, mechanic.Trigger));

        // enemy
        PluginContext.EnemyDataId = phase.TargetId;

        // update context (phase change)
        UpdateContext();
    }

    private void EmitMechanic(Mechanic mechanic)
    {
        completedCheckpoints.Add(mechanic.Name);

        // update progress
        var phase            = DutyConfig.Timeline.Phases[phaseIndex];
        var newSubphaseIndex = phase.CheckpointNames.IndexOf(mechanic.Name);
        if (newSubphaseIndex >= subphaseIndex)
            subphaseIndex = newSubphaseIndex;

        // emit event
        foreach (var action in mechanic.Actions)
            EmitAction(action);

        // check transition
        CheckTransition(mechanic);

        // update context (subphase and checkpoints change)
        UpdateContext();
    }

    private void EmitAction(Action action)
    {
        // update variables
        switch (action.Type)
        {
            case "INCREMENT_VARIABLE":
                if (variables.TryGetValue(action.Name, out var val) && val is long or int)
                    variables[action.Name] = Convert.ToInt64(val) + 1;
                break;
            case "SET_VARIABLE":
                variables[action.Name] = action.Value;
                break;
        }

        // check transition
        CheckTransition(action.Name);

        // update context (variables change)
        UpdateContext();
    }

    private void CheckTransition(Mechanic mechanic)
    {
        var phase = DutyConfig.Timeline.Phases[phaseIndex];
        foreach (var transition in phase.Transitions)
        {
            if (transition.Conditions
                          .Where(x => x.Type == "MECHANIC_TRIGGERED")
                          .Any(x => x.MechanicName == mechanic.Name))
            {
                EnterPhase(DutyConfig.Timeline.Phases.FindIndex(x => x.Name == transition.TargetPhase));
                return;
            }
        }
    }

    private void CheckTransition(string variable)
    {
        var phase = DutyConfig.Timeline.Phases[phaseIndex];
        foreach (var transition in phase.Transitions)
        {
            if (transition.Conditions
                          .Where(x => x.Type == "EXPRESSION")
                          .Any(x => x.Expression.Contains(variable) && CheckExpression(x.Expression)))
            {
                EnterPhase(DutyConfig.Timeline.Phases.FindIndex(x => x.Name == transition.TargetPhase));
                return;
            }
        }
    }

    private static bool CheckTrigger(Trigger trigger, IEvent? e = null)
    {
        switch (trigger.Type)
        {
            case "ACTION_EVENT":
                if (e is IActionEvent actionEvent)
                    return actionEvent.Match(trigger);
                return false;
            case "COMBATANT_EVENT":
                if (e is ICombatantEvent combatantEvent)
                    return combatantEvent.Match(trigger);
                return false;
            case "STATUS_EVENT":
                if (e is IStatusEvent statusEvent)
                    return statusEvent.Match(trigger);
                return false;
            default:
                return false;
        }
    }

    private bool CheckExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var parts = expression.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
            return false;

        var variablePath    = parts[0];
        var op              = parts[1];
        var literalValueStr = parts[2];

        if (!variablePath.StartsWith("variables."))
            return false;
        var variableName = variablePath.Substring("variables.".Length);

        if (!variables.TryGetValue(variableName, out var currentValueObj))
            return false;

        try
        {
            var currentValue = Convert.ToDouble(currentValueObj);
            var targetValue  = Convert.ToDouble(literalValueStr);

            return op switch
            {
                "==" => Math.Abs(currentValue - targetValue) < 0.05,
                "!=" => Math.Abs(currentValue - targetValue) > 0.05,
                ">" => currentValue > targetValue,
                ">=" => currentValue >= targetValue,
                "<" => currentValue < targetValue,
                "<=" => currentValue <= targetValue,
                _ => false
            };
        }
        catch (Exception) { return false; }
    }

    #endregion
}
