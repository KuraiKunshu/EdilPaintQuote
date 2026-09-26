using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using EdilPaintPreventibiviGen.Services;
using Microsoft.Web.WebView2.Core;

namespace EdilPaintPreventibiviGen.Views;

public partial class VeluxLoginWindow : Window
{
    public VeluxLoginWindow()
    {
        InitializeComponent();
        Loaded += OnBrowserLoaded;
    }

    private async void OnBrowserLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Preserve the existing browser profile for legacy installations.
            string? userDataFolder = CompanyInstallationService.IsGenericInstallation
                ? Path.Combine(CompanyInstallationService.RootDirectory, "Browser") : null;
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            if (!IsVisible) return;
            await WebView.EnsureCoreWebView2Async(environment);
            if (IsVisible) WebView.Source = new Uri("https://app.velux.it/cas/login");
        }
        catch (Exception ex)
        {
            if (IsVisible) MessageBox.Show(this, ex.Message, "Accesso Velux", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            WebView.CoreWebView2?.Stop();
            WebView.Source = null;
            WebView.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VELUX] WebView dispose error: {ex.Message}");
        }

        base.OnClosed(e);
    }

    private async void OnConfirmLoginClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (WebView.CoreWebView2 == null) {
                MessageBox.Show("Il browser non è ancora pronto.");
                return;
            }

            var cookieManager = WebView.CoreWebView2.CookieManager;
            var cookies = await cookieManager.GetCookiesAsync("https://app.velux.it");
            
            var cookieList = new List<object>();
            foreach (var cookie in cookies)
            {
                cookieList.Add(new {
                    name = cookie.Name,
                    value = cookie.Value,
                    domain = cookie.Domain,
                    path = cookie.Path
                });
            }

            var storage = new { cookies = cookieList };
            VeluxSessionStorage.Save(JsonSerializer.Serialize(storage, new JsonSerializerOptions { WriteIndented = true }));

            this.DialogResult = true;
            this.Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Errore durante il salvataggio della sessione: " + ex.Message);
        }
    }
}
