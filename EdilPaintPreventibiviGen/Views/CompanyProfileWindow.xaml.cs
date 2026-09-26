using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.Win32;

namespace EdilPaintPreventibiviGen.Views;

public partial class CompanyProfileWindow : Window
{
    private readonly ObservableCollection<string> _logos;
    private readonly int _minimumCounter;
    private string _stamp;
    private bool _legacyStamp;

    public CompanyProfileWindow(Company company)
    {
        InitializeComponent();
        TxtName.Text = company.Nome;
        TxtAddress.Text = company.Indirizzo;
        TxtVat.Text = company.Piva;
        TxtEmail.Text = company.Email;
        TxtPayment.Text = company.Termini_pagamento;
        _minimumCounter = Math.Max(0, company.Counter);
        TxtCounter.Text = _minimumCounter.ToString();
        _logos = new ObservableCollection<string>(company.Logo);
        CmbLogo.ItemsSource = _logos;
        CmbLogo.SelectedIndex = _logos.Count == 0 ? -1 : Math.Clamp(company.Logo_index, 0, _logos.Count - 1);
        _stamp = App.AppSettings.Business.StampFileName;
        _legacyStamp = App.AppSettings.Business.UseLegacyStamp;
        UpdateStampLabel();
    }

    private string? PickImage()
    {
        var picker = new OpenFileDialog { Filter = "Immagini PNG e JPG|*.png;*.jpg;*.jpeg", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return null;
        try { return CompanyBrandingService.ImportImage(picker.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Immagine non caricata", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private void OnLogoClick(object sender, RoutedEventArgs e)
    {
        string? name = PickImage();
        if (name == null) return;
        _logos.Add(name);
        CmbLogo.SelectedItem = name;
    }
    private void OnRemoveLogoClick(object sender, RoutedEventArgs e)
    {
        if (CmbLogo.SelectedItem is string logo) _logos.Remove(logo);
        if (_logos.Count > 0) CmbLogo.SelectedIndex = 0;
    }
    private void OnStampClick(object sender, RoutedEventArgs e)
    {
        string? name = PickImage();
        if (name == null) return;
        _stamp = name;
        _legacyStamp = false;
        UpdateStampLabel();
    }
    private void OnRemoveStampClick(object sender, RoutedEventArgs e)
    {
        _stamp = string.Empty;
        _legacyStamp = false;
        UpdateStampLabel();
    }
    private void UpdateStampLabel() => TxtStamp.Text = !string.IsNullOrWhiteSpace(_stamp)
        ? "Immagine aziendale caricata" : _legacyStamp ? "Timbro dell'installazione attuale" : "Nessuna immagine";

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show(this, "Inserisci la ragione sociale.", "Dati aziendali");
            TxtName.Focus();
            return;
        }
        if (!int.TryParse(TxtCounter.Text, out int counter) || counter < _minimumCounter)
        {
            MessageBox.Show(this, $"Il numero deve essere un intero uguale o maggiore di {_minimumCounter}.", "Numerazione");
            TxtCounter.Focus();
            return;
        }
        BtnSave.IsEnabled = false;
        try
        {
            var company = new Company
            {
                Nome = TxtName.Text.Trim(), Indirizzo = TxtAddress.Text.Trim(), Piva = TxtVat.Text.Trim(),
                Email = TxtEmail.Text.Trim(), Termini_pagamento = TxtPayment.Text.Trim(), Counter = counter,
                Logo = _logos.ToList(), Logo_index = Math.Max(0, CmbLogo.SelectedIndex)
            };
            await App.DataService.SaveCompanyAsync(company, Path.GetFileName(CmbLogo.SelectedItem as string ?? ""));
            var business = App.AppSettings.Business;
            string previousStamp = business.StampFileName;
            bool previousLegacy = business.UseLegacyStamp;
            business.StampFileName = _stamp;
            business.UseLegacyStamp = _legacyStamp;
            try { App.AppSettings.Save(); }
            catch
            {
                business.StampFileName = previousStamp;
                business.UseLegacyStamp = previousLegacy;
                throw new InvalidOperationException("Dati aziendali salvati, ma non è stato possibile salvare il timbro nelle impostazioni. Riprova.");
            }
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Salvataggio azienda", MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { BtnSave.IsEnabled = true; }
    }
}
