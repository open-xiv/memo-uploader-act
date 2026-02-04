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


namespace MemoUploader;

public class PluginMain : IActPluginV1
{
    private Label? lblStatus;

    // service
    private EventManager? eventService;

    // update cts
    private CancellationTokenSource? updateCts;

    public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
    {
        lblStatus              = pluginStatusText;
        pluginScreenSpace.Text = "酥卷 SuMemo";
        ((TabControl)pluginScreenSpace.Parent).TabPages.Remove(pluginScreenSpace);

        // path
        var pluginDir  = ActGlobals.oFormActMain.PluginGetSelfData(this).pluginFile.DirectoryName;
        var pluginPath = ActGlobals.oFormActMain.PluginGetSelfData(this).pluginFile.Name;

        // assembly resolver
        _ = new AssemblyResolver(new List<string>
        {
            pluginDir
        });

        // service
        eventService = new EventManager();
        eventService.Init();

        // link engine
        Context.OnFightFinalized += OnFightFinalized;

        // log file
        if (pluginDir is not null)
            LogHelper.LogPath = Path.Combine(pluginDir, "runtime.log");

        // parser
        ParseHelper.Init();

        lblStatus.Text = "初始化完成";

        // check for updates
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
            catch (OperationCanceledException)
            {
                // ignore
            }
        });
    }

    public void DeInitPlugin()
    {
        // unlink engine and services
        Context.OnFightFinalized -= OnFightFinalized;

        // service
        eventService?.Uninit();

        // cancel update
        updateCts?.Cancel();
        updateCts?.Dispose();

        if (lblStatus != null)
            lblStatus.Text = "插件已卸载";
    }

    private static void OnFightFinalized(FightRecordPayload payload) => _ = Task.Run(async () => await ApiClient.UploadFight(payload));
}
