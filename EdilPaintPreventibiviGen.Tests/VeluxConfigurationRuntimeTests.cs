using System.Net;
using System.Net.Http;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class VeluxConfigurationRuntimeTests
{
    [Fact]
    public async Task SettingsSwitchWaitsForTheActiveRequestAndThenUsesTheNewHostAndCookieScope()
    {
        var settings = new VeluxSettingsModel();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlers = new List<ProbeHandler>();
        var containers = new List<CookieContainer>();
        const string session = """
            {"cookies":[
              {"name":"old","value":"fixture","domain":"appvelux.devdemo.it","path":"/"},
              {"name":"new","value":"fixture","domain":"next-velux.example","path":"/"}
            ]}
            """;
        using var service = new VeluxService(() => settings, cookies =>
        {
            containers.Add(cookies);
            var handler = new ProbeHandler(async (request, token) =>
            {
                if (handlers.Count == 1)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token);
                }
                return Response(request, "[]");
            });
            handlers.Add(handler);
            return handler;
        }, () => session, () => 0);

        var first = service.SearchProductsAsync("GGL");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        settings = settings with { BaseUrl = "https://next-velux.example/" };
        var second = service.SearchProductsAsync("GGU");

        Assert.False(second.IsCompleted);
        Assert.False(Assert.Single(handlers).IsDisposed);
        release.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, handlers.Count);
        Assert.True(handlers[0].IsDisposed);
        Assert.Equal("appvelux.devdemo.it", Assert.Single(handlers[0].Requests).Host);
        Assert.Equal("next-velux.example", Assert.Single(handlers[1].Requests).Host);
        Assert.Equal("old", Assert.Single(containers[0].GetCookies(new Uri("https://appvelux.devdemo.it/")).Cast<Cookie>()).Name);
        Assert.Equal("new", Assert.Single(containers[1].GetCookies(new Uri("https://next-velux.example/")).Cast<Cookie>()).Name);
        Assert.Empty(containers[1].GetCookies(new Uri("https://appvelux.devdemo.it/")).Cast<Cookie>());
    }

    [Fact]
    public async Task RemovingTheSavedSessionAllowsTheLoginPromptAgainWithoutRestarting()
    {
        long sessionVersion = 0;
        int prompts = 0;
        using var service = new VeluxService(() => new VeluxSettingsModel(),
            _ => new ProbeHandler((request, _) => Task.FromResult(Response(request, "<form id='login-form'>Login</form>"))),
            () => null, () => sessionVersion);
        service.OnLoginRequired += settings =>
        {
            Assert.Equal(new VeluxSettingsModel().LoginUri, settings.LoginUri);
            prompts++;
            return Task.FromResult(false);
        };

        Assert.Empty(await service.SearchProductsAsync("GGL"));
        Assert.Empty(await service.SearchProductsAsync("GGU"));
        Assert.Equal(1, prompts);
        sessionVersion++;
        Assert.Empty(await service.SearchProductsAsync("GGL"));
        Assert.Equal(2, prompts);
    }

    [Fact]
    public async Task SuccessfulLoginIsRetriedOnlyOnceWhenTheServerStillReturnsTheLoginPage()
    {
        int requests = 0;
        int prompts = 0;
        using var service = new VeluxService(() => new VeluxSettingsModel(),
            _ => new ProbeHandler((request, _) =>
            {
                requests++;
                return Task.FromResult(Response(request, "<form id='login-form'>Login</form>"));
            }), () => null, () => 0);
        service.OnLoginRequired += _ =>
        {
            prompts++;
            return Task.FromResult(true);
        };

        Assert.Empty(await service.SearchProductsAsync("GGL").WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(2, requests);
        Assert.Equal(1, prompts);
    }

    private static HttpResponseMessage Response(HttpRequestMessage request, string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body), RequestMessage = request
    };

    private sealed class ProbeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public bool IsDisposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return send(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
