using System.Diagnostics;
using System.Reflection;
using EdilPaintPreventibiviGen.Android.Services;
using Npgsql;
using Xunit;

namespace EdilPaintPreventibiviGen.Android.Tests;

public class MobileDatabaseSafetyTests
{
    [Theory]
    [InlineData("postgresql://mobile:p%40ss%3Aword@demo.neon.tech/neondb?sslmode=disable")]
    [InlineData("Host=demo.neon.tech;Database=neondb;Username=mobile;Password=p@ss:word;SSL Mode=Disable")]
    public void NeonConnectionsVerifyCertificatesAndPreserveEncodedPasswords(string value)
    {
        var method = typeof(MobileDatabaseService).GetMethod("NormalizeConnectionString", BindingFlags.Static | BindingFlags.NonPublic)!;
        var normalized = new NpgsqlConnectionStringBuilder((string)method.Invoke(null, [value])!);
        Assert.Equal(SslMode.VerifyFull, normalized.SslMode);
        Assert.Equal("p@ss:word", normalized.Password);
        Assert.False(normalized.Pooling);
    }

    [Fact]
    public async Task CancellationFromCallerIsNotRetried()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        int calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Execute<int>(token =>
        {
            calls++;
            token.ThrowIfCancellationRequested();
            return Task.FromResult(1);
        }, source.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ReadHasOneTwentySecondDeadline()
    {
        var watch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<DatabaseReadTimeoutException>(() => Execute(async token =>
        {
            await Task.Delay(TimeSpan.FromMinutes(2), token);
            return 1;
        }));
        Assert.InRange(watch.Elapsed.TotalSeconds, 19, 24);
        Assert.Contains("20 secondi", MobileDatabaseService.GetUserMessage(exception));
    }

    [Fact]
    public async Task TransientReadFailuresRetryAtMostThreeTimes()
    {
        int calls = 0;
        await Assert.ThrowsAsync<IOException>(() => Execute<int>(_ =>
        {
            calls++;
            throw new IOException("connection reset");
        }));
        Assert.Equal(3, calls);
    }

    private static Task<T> Execute<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        var method = typeof(MobileDatabaseService).GetMethod("ExecuteReadWithRetryAsync", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(T));
        return (Task<T>)method.Invoke(null, [operation, cancellationToken])!;
    }
}
