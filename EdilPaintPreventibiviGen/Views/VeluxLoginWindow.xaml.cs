using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.Models;
using Microsoft.Web.WebView2.Core;

namespace EdilPaintPreventibiviGen.Views;

public partial class VeluxLoginWindow : Window
{
    private readonly VeluxSettingsModel _settings;

    public VeluxLoginWindow() : this(VeluxEndpoints.GetSettings()) { }

    public VeluxLoginWindow(VeluxSettingsModel settings)
    {
        _settings = settings.CreateValidatedCopy();
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
            if (IsVisible) WebView.Source = _settings.LoginUri;
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
            if (WebView.Source == null ||
                !string.Equals(WebView.Source.Host, _settings.BaseUri.Host, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(WebView.Source.AbsolutePath.TrimEnd('/'), _settings.LoginUri.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Completa l’accesso sul sito VELUX prima di confermare.",
                    "Accesso VELUX", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var cookieManager = WebView.CoreWebView2.CookieManager;
            var cookies = await cookieManager.GetCookiesAsync(_settings.CatalogUri.AbsoluteUri);
            var loginCookies = await cookieManager.GetCookiesAsync(_settings.LoginUri.AbsoluteUri);
            if (!IsVisible) return;
            
            var cookieList = new List<object>();
            foreach (var cookie in cookies.Concat(loginCookies).GroupBy(cookie => (cookie.Name, cookie.Domain, cookie.Path)).Select(group => group.First()))
            {
                cookieList.Add(new {
                    name = cookie.Name,
                    value = cookie.Value,
                    domain = cookie.Domain,
                    path = cookie.Path,
                    secure = cookie.IsSecure,
                    httpOnly = cookie.IsHttpOnly
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
