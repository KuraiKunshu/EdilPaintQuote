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
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
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
