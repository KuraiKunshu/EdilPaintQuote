using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;

namespace EdilPaintPreventibiviGen.Services;

public enum AutomaticUpdateRegistration
{
    Disabled,
    ScheduledTask,
    StartupFolder
}

public readonly record struct AutomaticUpdateStatus(
    AutomaticUpdateRegistration Registration,
    string Description)
{
    public bool IsEnabled => Registration != AutomaticUpdateRegistration.Disabled;
}

/// <summary>
/// Gestisce l'avvio dell'updater al login dell'utente corrente. La task usa gli
/// stessi nome e comportamento dello script di installazione, così le versioni
/// già configurate possono essere controllate direttamente dall'app.
/// </summary>
public static class UpdaterAutoUpdateService
{
    public const string TaskName = "EdilPaint Auto Update";

    private const int TaskActionExec = 0;
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskRunLevelLeastPrivilege = 0;
    private const int TaskTriggerLogon = 9;

    public static AutomaticUpdateStatus GetStatus()
    {
        if (!OperatingSystem.IsWindows())
            return new(AutomaticUpdateRegistration.Disabled, "Aggiornamenti automatici disponibili solo su Windows.");

        try
        {
            dynamic scheduler = CreateSchedulerService();
            dynamic rootFolder = scheduler.GetFolder("\\");
            dynamic task = rootFolder.GetTask($"\\{TaskName}");

            if (task != null && Convert.ToBoolean(task!.Enabled))
            {
                return new(
                    AutomaticUpdateRegistration.ScheduledTask,
                    "Attivi: il controllo viene eseguito a ogni accesso a Windows.");
            }
        }
        catch
        {
            // Se l'attività non esiste o l'Utilità di pianificazione non è
            // disponibile, verifichiamo il fallback nella cartella Esecuzione automatica.
        }

        if (GetExistingStartupFallbackPath() != null)
        {
            return new(
                AutomaticUpdateRegistration.StartupFolder,
                "Attivi: il controllo viene eseguito all'avvio dell'utente Windows.");
        }

        return new(AutomaticUpdateRegistration.Disabled, "Disattivati su questo PC.");
    }

    public static AutomaticUpdateStatus Enable(string updaterScriptPath)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Gli aggiornamenti automatici sono disponibili solo su Windows.");

        if (string.IsNullOrWhiteSpace(updaterScriptPath) || !File.Exists(updaterScriptPath))
            throw new FileNotFoundException("Script updater non trovato.", updaterScriptPath);

        string arguments = BuildUpdaterArguments(updaterScriptPath);

        try
        {
            dynamic scheduler = CreateSchedulerService();
            dynamic rootFolder = scheduler.GetFolder("\\");
            dynamic taskDefinition = scheduler.NewTask(0);

            taskDefinition.RegistrationInfo.Description =
                "Controlla GitHub e aggiorna EdilPaint Preventivi quando l'utente accede a Windows.";
            taskDefinition.Principal.LogonType = TaskLogonInteractiveToken;
            taskDefinition.Principal.RunLevel = TaskRunLevelLeastPrivilege;
            taskDefinition.Settings.Enabled = true;
            taskDefinition.Settings.AllowDemandStart = true;
            taskDefinition.Settings.StartWhenAvailable = true;
            taskDefinition.Settings.DisallowStartIfOnBatteries = false;
            taskDefinition.Settings.StopIfGoingOnBatteries = false;
            taskDefinition.Settings.ExecutionTimeLimit = "PT30M";

            dynamic trigger = taskDefinition.Triggers.Create(TaskTriggerLogon);
            trigger.Id = "Logon";

            dynamic action = taskDefinition.Actions.Create(TaskActionExec);
            action.Path = "powershell.exe";
            action.Arguments = arguments;
            action.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(updaterScriptPath));

            string userName = WindowsIdentity.GetCurrent().Name;
            rootFolder.RegisterTaskDefinition(
                TaskName,
                TaskCreateOrUpdate,
                userName,
                null,
                TaskLogonInteractiveToken,
                null);

            try
            {
                RemoveStartupFallback();
            }
            catch
            {
                // L'attività pianificata è già stata registrata con successo.
                // Un vecchio fallback non deve impedire l'attivazione.
            }

            return new(
                AutomaticUpdateRegistration.ScheduledTask,
                "Attivati: il controllo verrà eseguito a ogni accesso a Windows.");
        }
        catch (Exception scheduledTaskException)
        {
            try
            {
                CreateStartupFallback(arguments, updaterScriptPath);
                return new(
                    AutomaticUpdateRegistration.StartupFolder,
                    "Attivati: il controllo verrà eseguito all'avvio dell'utente Windows.");
            }
            catch (Exception startupException)
            {
                throw new InvalidOperationException(
                    "Impossibile configurare gli aggiornamenti automatici.",
                    new AggregateException(scheduledTaskException, startupException));
            }
        }
    }

    public static void Disable()
    {
        if (!OperatingSystem.IsWindows())
            return;

        bool scheduledTaskExists = ScheduledTaskExists();
        Exception? scheduledTaskException = null;
        try
        {
            dynamic scheduler = CreateSchedulerService();
            dynamic rootFolder = scheduler.GetFolder("\\");
            rootFolder.DeleteTask(TaskName, 0);
        }
        catch when (!scheduledTaskExists)
        {
            // L'assenza della task non è un errore: può essere attivo solo il fallback.
        }
        catch (Exception ex)
        {
            scheduledTaskException = ex;
        }

        try
        {
            RemoveStartupFallback();
        }
        catch (Exception startupException)
        {
            throw new InvalidOperationException(
                "Impossibile disattivare gli aggiornamenti automatici.",
                scheduledTaskException == null
                    ? startupException
                    : new AggregateException(scheduledTaskException, startupException));
        }

        if (scheduledTaskException != null)
        {
            throw new InvalidOperationException(
                "Impossibile disattivare l'attività pianificata degli aggiornamenti automatici.",
                scheduledTaskException);
        }
    }

    public static string BuildUpdaterArguments(string updaterScriptPath)
    {
        if (string.IsNullOrWhiteSpace(updaterScriptPath))
            throw new ArgumentException("Il percorso dello script updater è obbligatorio.", nameof(updaterScriptPath));

        string fullPath = Path.GetFullPath(updaterScriptPath);
        return $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File {QuoteArgument(fullPath)}";
    }

    private static dynamic CreateSchedulerService()
    {
        Type? schedulerType = Type.GetTypeFromProgID("Schedule.Service");
        if (schedulerType == null)
            throw new InvalidOperationException("Utilità di pianificazione di Windows non disponibile.");

        dynamic scheduler = Activator.CreateInstance(schedulerType)
            ?? throw new InvalidOperationException("Impossibile aprire l'Utilità di pianificazione di Windows.");
        scheduler.Connect();
        return scheduler;
    }

    private static bool ScheduledTaskExists()
    {
        try
        {
            dynamic scheduler = CreateSchedulerService();
            dynamic rootFolder = scheduler.GetFolder("\\");
            return rootFolder.GetTask($"\\{TaskName}") != null;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetExistingStartupFallbackPath()
    {
        foreach (string path in GetStartupFallbackPaths())
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static void CreateStartupFallback(string arguments, string updaterScriptPath)
    {
        string shortcutPath = GetStartupShortcutPath();
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
            throw new InvalidOperationException("Impossibile creare il collegamento di avvio automatico.");

        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Impossibile creare il collegamento di avvio automatico.");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = "powershell.exe";
        shortcut.Arguments = arguments;
        shortcut.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(updaterScriptPath));
        shortcut.WindowStyle = 7;
        shortcut.Description = "Controlla GitHub e aggiorna EdilPaint Preventivi quando l'utente accede a Windows.";
        shortcut.Save();

        string commandPath = Path.ChangeExtension(shortcutPath, ".cmd");
        if (File.Exists(commandPath))
            File.Delete(commandPath);
    }

    private static void RemoveStartupFallback()
    {
        foreach (string path in GetStartupFallbackPaths())
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static IEnumerable<string> GetStartupFallbackPaths()
    {
        string shortcutPath = GetStartupShortcutPath();
        yield return shortcutPath;
        yield return Path.ChangeExtension(shortcutPath, ".cmd");
    }

    private static string GetStartupShortcutPath()
    {
        string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (string.IsNullOrWhiteSpace(startupFolder))
            throw new InvalidOperationException("Cartella Esecuzione automatica di Windows non disponibile.");

        Directory.CreateDirectory(startupFolder);
        return Path.Combine(startupFolder, $"{TaskName}.lnk");
    }

    private static string QuoteArgument(string value)
        => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
