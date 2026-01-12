using System.Linq;
using Advanced_Combat_Tracker;


namespace MemoUploader.Helpers;

internal static class ParseHelper
{
    public static FFXIV_ACT_Plugin.FFXIV_ACT_Plugin? Parser;

    public static void Init()
    {
        if (ActGlobals.oFormActMain is null)
        {
            Parser = null;
            return;
        }

        if (Parser is not null)
            return;

        var pluginData = ActGlobals.oFormActMain.ActPlugins.FirstOrDefault(x => x.pluginObj?.GetType().ToString() == "FFXIV_ACT_Plugin.FFXIV_ACT_Plugin");
        if (pluginData is null)
            return;

        Parser = (FFXIV_ACT_Plugin.FFXIV_ACT_Plugin)pluginData.pluginObj;
    }
}
