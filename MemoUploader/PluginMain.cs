using System;
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
        AssemblyResolver asmResolver = new AssemblyResolver(new List<string>
        {
            ActGlobals.oFormActMain.PluginGetSelfData(this).pluginFile.DirectoryName,
        });

        // engine
        engine = new RuleEngine();

        // service
        eventService = new EventManager();
        eventService.Init();

        // link engine and services
        eventService.OnEvent += engine.PostEvent;

        lblStatus.Text = "初始化完成";

        // check for updates
        updateCts = new CancellationTokenSource();
        var pluginPath   = ActGlobals.oFormActMain.ActPlugins.Find(p => string.Equals(p.pluginFile.Name, "MemoUploader.dll"))?.pluginFile.FullName;
        var updateHelper = new UpdateHelper(pluginPath);
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
