using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public class VeluxResult
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class VeluxService : IDisposable
{
    private HttpClient _httpClient = null!;
    private CookieContainer _cookieContainer = null!;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private bool _loginPromptShown;
    public event Func<Task<bool>>? OnLoginRequired;

    public VeluxService() => InitializeClient();

    private void InitializeClient()
    {
        _httpClient?.Dispose();
        _cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler { CookieContainer = _cookieContainer, AllowAutoRedirect = true };
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        LoadSessionCookies();
    }

    private void LoadSessionCookies()
    {
        try {
            var json = VeluxSessionStorage.Load();
            if (string.IsNullOrWhiteSpace(json)) return;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("cookies", out var cookies)) {
                foreach (var c in cookies.EnumerateArray()) {
                    string domain = c.GetProperty("domain").GetString()!.TrimStart('.');
                    _cookieContainer.Add(new Uri("https://app.velux.it"), new Cookie(c.GetProperty("name").GetString()!, c.GetProperty("value").GetString()!, "/", domain));
                }
            }
        } catch { }
    }

    public async Task<List<VeluxResult>> SearchProductsAsync(
        string term,
        CancellationToken cancellationToken = default)
    {
        try {
            if (AppShutdownManager.IsShutdownRequested)
                return new List<VeluxResult>();

            var url = $"https://app.velux.it/preventivi/it/backoffice/line_items/autocomplete_product_code?term={Uri.EscapeDataString(term)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (content.Contains("<form", StringComparison.OrdinalIgnoreCase) &&
                content.Contains("login", StringComparison.OrdinalIgnoreCase)) {
                if (await TryLoginOnceAsync(cancellationToken)) {
                    InitializeClient(); 
                    return await SearchProductsAsync(term, cancellationToken);
                }
                return new List<VeluxResult>();
            }
            return JsonSerializer.Deserialize<List<VeluxResult>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            Debug.WriteLine($"[VELUX SEARCH] {ex.Message}");
            return new List<VeluxResult>();
        }
    }

    private async Task<bool> TryLoginOnceAsync(CancellationToken cancellationToken)
    {
        if (AppShutdownManager.IsShutdownRequested)
            return false;

        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            if (_loginPromptShown || OnLoginRequired == null)
                return false;

            _loginPromptShown = true;
            return await OnLoginRequired.Invoke();
        }
        finally
        {
            _loginLock.Release();
        }
    }

    public async Task<Item?> GetProductDetailsAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        try {
            if (AppShutdownManager.IsShutdownRequested)
                return null;

            var url = $"https://app.velux.it/preventivi/it/backoffice/product_data?id={uuid}&description_type=false&description_change=false";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            // "Simuliamo" il parser jQuery estraendo tutte le coppie ID/Valore in un dizionario
            var dataMap = ParseJQueryAssignments(content);

            var item = new Item { Quantity = 1 };

            // Ora preleviamo i dati dal dizionario usando le chiavi che abbiamo visto nel debug
            item.Name = dataMap.GetValueOrDefault("line_item_name", "Prodotto Velux");
            item.Description = dataMap.GetValueOrDefault("line_item_description", "");
            
            if (dataMap.TryGetValue("line_item_price_cents", out string? priceCents) && 
                double.TryParse(priceCents, out double cents))
            {
                item.UnitPrice = cents / 100;
            }

            Debug.WriteLine($"[VELUX] Product details parsed: {item.Name}");
            return item;
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            Debug.WriteLine($"[VELUX PARSE ERROR] {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Metodo che "pulisce" l'output jQuery ed estrae i valori.
    /// Cerca pattern come: $('#id').val('valore');
    /// </summary>
    private Dictionary<string, string> ParseJQueryAssignments(string script)
    {
        var dict = new Dictionary<string, string>();
        
        // Regex migliorata:
        // Group 1: l'ID del campo (senza #)
        // Group 2: il valore contenuto tra apici (singoli o doppi)
        string pattern = @"\$\('#(?<id>[^']*)'\)\.val\(['""](?<val>.*?)['""]\)";
        
        var matches = Regex.Matches(script, pattern, RegexOptions.Singleline);
        foreach (Match m in matches)
        {
            string id = m.Groups["id"].Value;
            string val = m.Groups["val"].Value;
            dict[id] = val;
        }

        return dict;
    }

    public void Dispose()
    {
        try
        {
            _httpClient.CancelPendingRequests();
        }
        catch
        {
        }

        _httpClient.Dispose();
        _loginLock.Dispose();
    }
}
