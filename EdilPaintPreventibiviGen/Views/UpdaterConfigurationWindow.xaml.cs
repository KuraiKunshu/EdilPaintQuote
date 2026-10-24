using System.IO;
using System.Windows;
using EdilPaintPreventibiviGen.Services;

namespace EdilPaintPreventibiviGen.Views;

public partial class UpdaterConfigurationWindow : Window
{
    private string? _existingScript;
    public string? ScriptPath { get; private set; }

    public UpdaterConfigurationWindow()
    {
        InitializeComponent();
        Helpers.WindowResizeBehavior.PreventMaximizedState(this);
        TxtInstallation.Text = AppContext.BaseDirectory;
        TxtVersion.Text = UpdaterConfigurationService.DescribeVersion(AppContext.BaseDirectory);
        TxtDevelopmentNotice.Visibility = UpdaterConfigurationService.IsDevelopmentDirectory(AppContext.BaseDirectory)
            ? Visibility.Visible : Visibility.Collapsed;
        TxtRepository.Text = UpdaterConfigurationService.DefaultRepository;
        TxtBranch.Text = "main";
        string? script = UpdaterLauncherService.ResolveUpdaterScriptPath();
        if (script == null || !File.Exists(Path.Combine(Path.GetDirectoryName(script)!, UpdaterConfigurationService.SettingsName))) return;
        try
        {
            var configuration = UpdaterConfigurationService.Read(script, AppContext.BaseDirectory);
            _existingScript = script;
            TxtRepository.Text = configuration.RepositoryUrl;
            TxtBranch.Text = configuration.Branch;
            ChkRunTests.IsChecked = configuration.RunTests;
        }
        catch (Exception ex)
        {
            TxtError.Text = $"La configurazione precedente non è utilizzabile: {ex.Message}\nSalvando ne verrà creata una per questa copia del programma.";
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ScriptPath = UpdaterConfigurationService.Save(AppContext.BaseDirectory, TxtRepository.Text,
                TxtBranch.Text, ChkRunTests.IsChecked == true, _existingScript);
            DialogResult = true;
        }
        catch (Exception ex) { TxtError.Text = ex.Message; }
    }
}
