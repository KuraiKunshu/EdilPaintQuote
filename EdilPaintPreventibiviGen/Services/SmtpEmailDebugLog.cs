using System.Diagnostics;
using System.IO;
using System.Text;

namespace EdilPaintPreventibiviGen.Services;

public sealed class SmtpEmailDebugLog
{
    private static readonly object FileSync = new();
    private readonly List<string> _filePaths;

    public string FilePath { get; }

    public SmtpEmailDebugLog()
    {
        _filePaths = ResolveLogFilePaths();
        FilePath = _filePaths[0];
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
                foreach (string filePath in _filePaths)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                    File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SMTP] Impossibile scrivere il log SMTP: {ex.Message}");
        }
    }

    public static string WriteFailure(string context, Exception exception)
    {
        var log = new SmtpEmailDebugLog();
        log.Separator();
        log.Write(context);
        log.Write($"ERRORE: {exception.GetType().Name}: {exception.Message}");
        if (exception.InnerException != null)
            log.Write($"INNER: {exception.InnerException.GetType().Name}: {exception.InnerException.Message}");

        return log.FilePath;
    }

    public static string GetPrimaryLogDirectory() =>
        Path.Combine(LocalApplicationDataService.GetDataDirectoryPath(), "MailLogs");

    private static List<string> ResolveLogFilePaths()
    {
        string fileName = $"smtp-{DateTime.Now:yyyyMMdd}.log";
        string primaryDirectory = GetPrimaryLogDirectory();
        Directory.CreateDirectory(primaryDirectory);
        var paths = new List<string>
        {
            Path.Combine(primaryDirectory, fileName)
        };

        string appLogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MailLogs");
        if (CanWriteToDirectory(appLogDirectory))
        {
            string appLogPath = Path.Combine(appLogDirectory, fileName);
            if (!paths.Contains(appLogPath, StringComparer.OrdinalIgnoreCase))
                paths.Add(appLogPath);
        }

        return paths;
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
