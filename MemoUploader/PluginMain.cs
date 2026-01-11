using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using MemoUploader.Engine;
using MemoUploader.Events;
using MemoUploader.Helpers;


namespace MemoUploader;

public class PluginMain : IActPluginV1
{
    private Label lblStatus;

    // service
    private RuleEngine   engine;
    private EventManager eventService;

    // update cts
    private CancellationTokenSource updateCts;

    public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
    {
        lblStatus              = pluginStatusText;
        pluginScreenSpace.Text = "酥卷 SuMemo";
        ((TabControl)pluginScreenSpace.Parent).TabPages.Remove(pluginScreenSpace);

        // path
        var pluginDir  = ActGlobals.oFormActMain.PluginGetSelfData(this).pluginFile.DirectoryName;
        var pluginPath = ActGlobals.oFormActMain.PluginGetSelfData(this).pluginFile.Name;

        // assembly resolver
        var asmResolver = new AssemblyResolver(new List<string>
        {
            pluginDir
        });

        // engine
        engine = new RuleEngine();

        // service
        eventService = new EventManager();
        eventService.Init();

        // link engine and services
        eventService.OnEvent += engine.PostEvent;

        // log file
        if (pluginDir is not null)
            LogHelper.LogPath = Path.Combine(pluginDir, "runtime.log");

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
        eventService.OnEvent -= engine.PostEvent;

        // service
        eventService.Uninit();

        // cancel update
        updateCts?.Cancel();
        updateCts?.Dispose();

        lblStatus.Text = "插件已卸载";
    }
}
