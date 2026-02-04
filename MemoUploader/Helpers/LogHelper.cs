using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;


namespace MemoUploader.Helpers;

internal static class LogHelper
{
    private static RichTextBox? LogBox { get; set; }

    public static string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Plugins",
        "SumemoUploader.log"
    );

    private static readonly object FileLock = new();

    // Maximum size (in bytes) before rotating the log file. Default: 5 MB.
    private static long MaxFileSizeBytes { get; } = 5 * 1024 * 1024;

    // How many rotated backups to keep. Oldest will be deleted when exceeded.
    private static int MaxBackupFiles { get; } = 5;

    public static void Init(RichTextBox logBox)
    {
        LogBox            = logBox;
        LogBox.Dock       = DockStyle.Fill;
        LogBox.ReadOnly   = true;
        LogBox.Font       = new Font("Consolas", 10);
        LogBox.ScrollBars = RichTextBoxScrollBars.Vertical;
    }

    public static void Uninit()
        => LogBox = null;

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
        var logStr = $"[{DateTime.Now:MM-dd HH:mm:ss}] [{level}] {message}";

        try
        {
            // write to file
            lock (FileLock)
            {
                try
                {
                    if (File.Exists(LogPath))
                    {
                        try
                        {
                            var fi = new FileInfo(LogPath);
                            if (fi.Length > MaxFileSizeBytes)
                            {
                                var dir        = Path.GetDirectoryName(LogPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                                var nameNoExt  = Path.GetFileNameWithoutExtension(LogPath);
                                var ext        = Path.GetExtension(LogPath);
                                var timestamp  = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                                var backupName = Path.Combine(dir, $"{nameNoExt}_{timestamp}{ext}");

                                File.Move(LogPath, backupName);

                                using (var sw = new StreamWriter(LogPath, false, Encoding.UTF8))
                                {
                                    sw.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Log rotated. Previous log: {Path.GetFileName(backupName)}");
                                }

                                try
                                {
                                    var pattern = $"{nameNoExt}_*{ext}";
                                    var backups = Directory.GetFiles(dir, pattern);
                                    if (backups.Length > MaxBackupFiles)
                                    {
                                        Array.Sort(backups, StringComparer.Ordinal);
                                        var toDeleteCount = backups.Length - MaxBackupFiles;
                                        for (var i = 0; i < toDeleteCount; i++)
                                        {
                                            try { File.Delete(backups[i]); }
                                            catch
                                            {
                                                /* ignored */
                                            }
                                        }
                                    }
                                }
                                catch
                                {
                                    // ignore
                                }
                            }
                        }
                        catch
                        {
                            // ignore
                        }
                    }

                    using (var sw = new StreamWriter(LogPath, true, Encoding.UTF8))
                        sw.WriteLine(logStr);
                }
                catch
                {
                    // ignore
                }
            }

            // write to ui
            if (LogBox is null || LogBox.IsDisposed)
                return;

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
