using System.Diagnostics;
using System.IO;
using System.Text;

namespace EdilPaintPreventibiviGen.Services;

public sealed class SmtpEmailDebugLog
{
    private static readonly object FileSync = new();

    public string FilePath { get; }

    public SmtpEmailDebugLog()
    {
        string logDirectory = ResolveLogDirectory();
        Directory.CreateDirectory(logDirectory);
        FilePath = Path.Combine(logDirectory, $"smtp-{DateTime.Now:yyyyMMdd}.log");
    }

    public void Separator()
    {
        Write("------------------------------------------------------------");
    }

    public void Write(string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        Debug.WriteLine("[SMTP] " + message);

        try
        {
            lock (FileSync)
            {
                File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SMTP] Impossibile scrivere il log SMTP: {ex.Message}");
        }
    }

    private static string ResolveLogDirectory()
    {
        string appLogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MailLogs");
        if (CanWriteToDirectory(appLogDirectory))
            return appLogDirectory;

        return Path.Combine(LocalApplicationDataService.GetDataDirectoryPath(), "MailLogs");
    }

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string testPath = Path.Combine(directory, ".writetest");
            File.WriteAllText(testPath, "test", Encoding.UTF8);
            File.Delete(testPath);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SMTP] Cartella log non scrivibile '{directory}': {ex.Message}");
            return false;
        }
    }
}
