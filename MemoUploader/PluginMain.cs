using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using MemoEngine;
using MemoEngine.Models;
using MemoUploader.Api;
using MemoUploader.Events;
using MemoUploader.Helpers;
using MemoUploader.Tags;


namespace MemoUploader;

public class PluginMain : IActPluginV1
{
    private Label? lblStatus;

    private EventManager? eventService;

    private CancellationTokenSource? updateCts;

    public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
    {
        lblStatus              = pluginStatusText;
        pluginScreenSpace.Text = "酥卷 SuMemo";
        ((TabControl)pluginScreenSpace.Parent).TabPages.Remove(pluginScreenSpace);

        var pluginDir  = ActGlobals.oFormActMain.PluginGetSelfData(this).pluginFile.DirectoryName;
        var pluginPath = ActGlobals.oFormActMain.PluginGetSelfData(this).pluginFile.Name;

        _ = new AssemblyResolver(new List<string>
        {
            pluginDir
        });

        eventService = new EventManager();
        eventService.Init();

        Context.OnFightFinalized += OnFightFinalized;

        if (pluginDir is not null)
            LogHelper.LogPath = Path.Combine(pluginDir, "runtime.log");

        ParseHelper.Init();

        // RouletteTracker subscribes to FFXIV_ACT_Plugin.DataSubscription,
        // which is only reachable after ParseHelper resolves the parser.
        RouletteTracker.Init();

        lblStatus.Text = "初始化完成";

        updateCts = new CancellationTokenSource();
        var updateHelper = new UpdateHelper(pluginPath, pluginDir);
        _ = Task.Run(async () =>
        {
            try
            {
                await updateHelper.CheckForUpdatesAsync(updateCts.Token);
                if (updateHelper.HasUpdate)
                    await updateHelper.PerformUpdateAsync(updateCts.Token);
            }
            catch (OperationCanceledException) { }
        });
    }

    public void DeInitPlugin()
    {
        Context.OnFightFinalized -= OnFightFinalized;

        RouletteTracker.Uninit();

        eventService?.Uninit();

        updateCts?.Cancel();
        updateCts?.Dispose();

        if (lblStatus != null)
            lblStatus.Text = "插件已卸载";
    }

    private static void OnFightFinalized(FightRecordPayload payload) => _ = Task.Run(async () => await ApiClient.UploadFight(payload));
}
