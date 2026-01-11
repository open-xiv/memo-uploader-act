using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;


namespace MemoUploader.Helpers;

public static class LogHelper
{
    public static RichTextBox LogBox { get; set; }

    static LogHelper()
    {
        LogBox            = new RichTextBox();
        LogBox.Dock       = DockStyle.Fill;
        LogBox.ReadOnly   = true;
        LogBox.Font       = new Font("Consolas", 10);
        LogBox.ScrollBars = RichTextBoxScrollBars.Vertical;
    }

    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "SumemoUploader_Debug.log"
    );

    private static readonly object FileLock = new();

    public static void Debug(string message)
        => Write("DEBUG", message);

    public static void Warning(string message)
        => Write("WARN", message);

    public static void Info(string message)
        => Write("INFO", message);

    public static void Error(string message, Exception? ex = null)
    {
        var msg = ex != null ? $"{message}\n{ex}" : message;
        Write("ERROR", msg);
    }

    private static void Write(string level, string message)
    {
        var logStr = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";

        try
        {
            // write to file
            lock (FileLock)
                using (var sw = new StreamWriter(LogPath, true, Encoding.UTF8))
                    sw.WriteLine(logStr);

            // write to ui
            if (LogBox.InvokeRequired)
            {
                LogBox.Invoke(new Action(() => Write(level, message)));
                return;
            }

            LogBox.AppendText(logStr + Environment.NewLine);
            LogBox.ScrollToCaret();
        }
        catch
        {
            // ignored
        }
    }
}
