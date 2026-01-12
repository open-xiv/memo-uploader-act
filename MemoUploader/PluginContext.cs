using System.Collections.Generic;
using MemoUploader.Models;


namespace MemoUploader;

internal static class PluginContext
{
    // recorder
    internal static IReadOnlyList<EventLog> EventHistory { get; set; } = [];

    // fight context
    internal static EngineState?                         Lifecycle       { get; set; }
    internal static string                               CurrentPhase    { get; set; } = string.Empty;
    internal static string                               CurrentSubphase { get; set; } = string.Empty;
    internal static uint                                 EnemyDataId     { get; set; }
    internal static IReadOnlyList<(string, bool)>        Checkpoints     { get; set; } = [];
    internal static IReadOnlyDictionary<string, object?> VariableStats   { get; set; } = new Dictionary<string, object?>();
}
