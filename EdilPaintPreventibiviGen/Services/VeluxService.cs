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
    private HttpClient? _httpClient;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<VeluxSettingsModel> _getSettings;
    private readonly Func<CookieContainer, HttpMessageHandler> _createHandler;
    private readonly Func<string?> _loadSession;
    private readonly Func<long> _getSessionVersion;
    private VeluxSettingsModel? _settings;
    private long _sessionVersion = -1;
    private bool _loginPromptShown;
    private bool _disposed;
    public event Func<VeluxSettingsModel, Task<bool>>? OnLoginRequired;

    public VeluxService() : this(VeluxEndpoints.GetSettings,
        cookies => new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = true },
        VeluxSessionStorage.Load, () => VeluxSessionStorage.SessionVersion)
    {
    }

    internal VeluxService(Func<VeluxSettingsModel> getSettings, Func<CookieContainer, HttpMessageHandler> createHandler,
        Func<string?> loadSession, Func<long> getSessionVersion)
    {
        _getSettings = getSettings;
        _createHandler = createHandler;
        _loadSession = loadSession;
        _getSessionVersion = getSessionVersion;
    }

    private void InitializeClient(VeluxSettingsModel settings)
    {
        _httpClient?.Dispose();
        var cookies = new CookieContainer();
        try {
            var json = _loadSession();
            if (!string.IsNullOrWhiteSpace(json))
                VeluxSessionStorage.RestoreCookies(cookies, json, settings.BaseUri);
        } catch { }
        _httpClient = new HttpClient(_createHandler(cookies)) { Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        _settings = settings;
        _sessionVersion = _getSessionVersion();
    }

    public async Task<List<VeluxResult>> SearchProductsAsync(
        string term,
        CancellationToken cancellationToken = default)
    {
        try {
            var content = await GetCatalogResponseAsync(settings => settings.SearchProductsUri(term), cancellationToken);
            return content == null ? [] : JsonSerializer.Deserialize<List<VeluxResult>>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            Debug.WriteLine($"[VELUX SEARCH] {ex.Message}");
            return new List<VeluxResult>();
        }
    }

    private async Task<string?> GetCatalogResponseAsync(Func<VeluxSettingsModel, Uri> requestUri, CancellationToken cancellationToken)
    {
        if (_disposed || AppShutdownManager.IsShutdownRequested) return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        await _requestLock.WaitAsync(token);
        try
        {
            var settings = _getSettings().CreateValidatedCopy();
            if (_settings != settings || _sessionVersion != _getSessionVersion())
            {
                InitializeClient(settings);
                _loginPromptShown = false;
            }
            for (int attempt = 0; attempt < 2; attempt++)
            {
                token.ThrowIfCancellationRequested();
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri(settings));
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                using var response = await _httpClient!.SendAsync(request, token);
                var content = await response.Content.ReadAsStringAsync(token);
                if (!IsLoginResponse(response, content, settings))
                {
                    response.EnsureSuccessStatusCode();
                    return content;
                }
                if (_loginPromptShown || OnLoginRequired == null) return null;
                _loginPromptShown = true;
                if (!await OnLoginRequired.Invoke(settings)) return null;
                token.ThrowIfCancellationRequested();
                InitializeClient(settings);
            }
            return null;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    public async Task<Item?> GetProductDetailsAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        try {
            var content = await GetCatalogResponseAsync(settings => settings.ProductDetailsUri(uuid), cancellationToken);
            return content == null ? null : ParseProductDetails(content);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            Debug.WriteLine($"[VELUX PARSE ERROR] {ex.Message}");
            return null;
        }
    }

    private static bool IsLoginResponse(HttpResponseMessage response, string content, VeluxSettingsModel settings) =>
        response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ||
        string.Equals(response.RequestMessage?.RequestUri?.AbsolutePath.TrimEnd('/'), settings.LoginUri.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ||
        (content.Contains("<form", StringComparison.OrdinalIgnoreCase) && content.Contains("login", StringComparison.OrdinalIgnoreCase));

    internal static Item? ParseProductDetails(string content)
    {
        var data = ParseJQueryAssignments(content);
        if (!data.TryGetValue("line_item_name", out string? name) || string.IsNullOrWhiteSpace(name))
            return null;

        var item = new Item { Quantity = 1, Name = name, Description = data.GetValueOrDefault("line_item_description", "") };
        if (data.TryGetValue("line_item_price_cents", out string? priceCents) && double.TryParse(priceCents, out double cents))
            item.UnitPrice = cents / 100;
        return item;
    }

    /// <summary>
    /// Metodo che "pulisce" l'output jQuery ed estrae i valori.
    /// Cerca pattern come: $('#id').val('valore');
    /// </summary>
    private static Dictionary<string, string> ParseJQueryAssignments(string script)
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
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _httpClient?.Dispose();
        _lifetime.Dispose();
        // Queued requests release the semaphore after cancellation, so it must remain usable until they finish.
    }
}
