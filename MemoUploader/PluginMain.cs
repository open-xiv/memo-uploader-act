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

    public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
    {
        lblStatus              = pluginStatusText;
        pluginScreenSpace.Text = "酥卷 SuMemo";
        pluginScreenSpace.Controls.Add(LogHelper.LogBox);
        ((TabControl)(pluginScreenSpace.Parent)).TabPages.Remove(pluginScreenSpace);

        // engine
        engine = new RuleEngine();

        // service
        eventService = new EventManager();
        eventService.Init();

        // link engine and services
        eventService.OnEvent += engine.PostEvent;

        lblStatus.Text = "初始化完成";
    }

    /// <summary>
    ///     插件反初始化
    /// </summary>
    public void DeInitPlugin()
    {
        // unlink engine and services
        eventService.OnEvent -= engine.PostEvent;

        // service
        eventService.Uninit();

        lblStatus.Text = "插件已卸载";
    }
}
