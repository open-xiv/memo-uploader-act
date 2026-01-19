using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using MemoUploader.Api;
using MemoUploader.Helpers;
using MemoUploader.Models;


namespace MemoUploader.Engine;

internal class RuleEngine
{
    // event queue
    private readonly ActionBlock<IEvent> eventQueue;

    // event history
    private readonly EventRecorder eventHistory = new(1000);

    // fight context
    private FightContext? fightContext;

    public RuleEngine()
    {
        eventQueue = new ActionBlock<IEvent>(ProcessEventAsync, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });
    }


    public void PostEvent(IEvent e)
        => eventQueue.Post(e);

    private async Task ProcessEventAsync(IEvent e)
    {
        // event logs
        eventHistory.Record(e);

        if (e is TerritoryChanged tc)
        {
            var dutyConfig = await ApiClient.FetchDuty(tc.ZoneId);
            if (dutyConfig is not null)
            {
                if (fightContext is not null)
                    LogHelper.Info($"Force ending previous fight context: {fightContext.DutyConfig.ZoneId} -> {dutyConfig.ZoneId}");
                fightContext = new FightContext(dutyConfig);
            }
        }

        fightContext?.ProcessEvent(e);
    }
}
