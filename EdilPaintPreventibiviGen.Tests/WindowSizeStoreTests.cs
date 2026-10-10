using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WindowSizeStoreTests
{
    [Fact]
    public void SavedDimensionsAndMaximizedStateSurviveRestartWithoutSharingMutableObjects()
    {
        using var scope = new TestDirectory();
        var store = new WindowSizeStore(scope.Path);
        var selected = new WindowSizePreference { Width = 1250.25, Height = 900.5, IsMaximized = true };
        Assert.True(store.Save("MainWindow", selected));
        selected.Width = 1;

        var restored = Assert.IsType<WindowSizePreference>(new WindowSizeStore(scope.Path).Get("MainWindow"));
        Assert.Equal(1250.25, restored.Width);
        Assert.Equal(900.5, restored.Height);
        Assert.True(restored.IsMaximized);
        restored.Height = 1;
        Assert.Equal(900.5, store.Get("MainWindow")!.Height);
        Assert.DoesNotContain("IsValid", File.ReadAllText(scope.Path));
    }

    [Fact]
    public void UpdatingOneWindowPreservesOtherTypesAndReloadsWritesFromAnotherInstance()
    {
        using var scope = new TestDirectory();
        var first = new WindowSizeStore(scope.Path);
        var second = new WindowSizeStore(scope.Path);
        Assert.Null(first.Get("Calendar"));
        Assert.True(first.Save("Calendar", Preference(1280, 820)));
        Assert.True(second.Save("History", Preference(1400, 760)));
        Assert.True(first.Save("Calendar", Preference(1450, 900)));

        Assert.Equal(1450, second.Get("Calendar")!.Width);
        Assert.Equal(1400, new WindowSizeStore(scope.Path).Get("History")!.Width);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(scope.Path));
        Assert.Equal(2, document.RootElement.GetProperty("Windows").EnumerateObject().Count());
    }

    [Fact]
    public async Task ConcurrentInstancesPreserveEveryWindowAndLeaveNoTemporaryFiles()
    {
        using var scope = new TestDirectory();
        var stores = new[] { new WindowSizeStore(scope.Path), new WindowSizeStore(scope.Path) };
        await Task.WhenAll(Enumerable.Range(0, 24).Select(index => Task.Run(() =>
            Assert.True(stores[index % 2].Save("Window" + index, Preference(1000 + index, 700 + index))))));

        var reopened = new WindowSizeStore(scope.Path);
        for (int index = 0; index < 24; index++)
            Assert.Equal(1000 + index, reopened.Get("Window" + index)!.Width);
        Assert.Empty(Directory.GetFiles(scope.Root, "*.tmp"));
    }

    [Fact]
    public void InvalidEntriesDoNotDiscardOtherWindows()
    {
        using var scope = new TestDirectory();
        File.WriteAllText(scope.Path, """
            {
              "Version": 1,
              "Windows": {
                "MainWindow": { "Width": 1250, "Height": 900, "IsMaximized": true },
                "Zero": { "Width": 0, "Height": 900 },
                "Negative": { "Width": 700, "Height": -1 },
                "Missing": { "Width": 800 },
                "Text": { "Width": "bad", "Height": 700 },
                "Overflow": { "Width": 1e500, "Height": 700 },
                "WrongState": { "Width": 800, "Height": 700, "IsMaximized": "bad" },
                "Null": null,
                "Array": [],
                "   ": { "Width": 800, "Height": 700 },
                "Calendar": { "Width": 1280, "Height": 820 }
              }
            }
            """);
        var store = new WindowSizeStore(scope.Path);
        Assert.True(store.Get("MainWindow")!.IsMaximized);
        Assert.Equal(1280, store.Get("Calendar")!.Width);
        foreach (string key in new[] { "Zero", "Negative", "Missing", "Text", "Overflow", "WrongState", "Null", "Array", "   " })
            Assert.Null(store.Get(key));
        Assert.True(store.Save("Notes", Preference(650, 700)));
        Assert.Equal(900, new WindowSizeStore(scope.Path).Get("MainWindow")!.Height);
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"Version\":\"bad\",\"Windows\":{}}")]
    [InlineData("{\"Version\":2,\"Windows\":{\"MainWindow\":{\"Width\":1200,\"Height\":900}}}")]
    [InlineData("{\"Version\":1,\"Windows\":[]}")]
    public void MalformedOrUnsupportedFilesFallBackWithoutBlockingNewPreferences(string content)
    {
        using var scope = new TestDirectory();
        File.WriteAllText(scope.Path, content);
        var store = new WindowSizeStore(scope.Path);
        Assert.Null(store.Get("MainWindow"));
        Assert.True(store.Save("Calendar", Preference(1280, 820)));
        Assert.Equal(1280, new WindowSizeStore(scope.Path).Get("Calendar")!.Width);
    }

    [Fact]
    public void MissingFileAndParentDirectoryAreCreatedOnlyWhenSaving()
    {
        using var scope = new TestDirectory();
        string nestedPath = System.IO.Path.Combine(scope.Root, "nested", "size.json");
        var store = new WindowSizeStore(nestedPath);
        Assert.Null(store.Get("MainWindow"));
        Assert.False(Directory.Exists(System.IO.Path.GetDirectoryName(nestedPath)));
        Assert.True(store.Save("MainWindow", Preference(1250, 900)));
        Assert.Equal(1250, new WindowSizeStore(nestedPath).Get("MainWindow")!.Width);
    }

    [Fact]
    public void FailedWritesRemainAvailableAndAreRetriedWithTheNextWindow()
    {
        using var scope = new TestDirectory();
        string blockedDirectory = System.IO.Path.Combine(scope.Root, "blocked");
        File.WriteAllText(blockedDirectory, "file prevents directory creation");
        string settingsPath = System.IO.Path.Combine(blockedDirectory, "size.json");
        var store = new WindowSizeStore(settingsPath);
        Assert.False(store.Save("Calendar", Preference(1280, 820)));
        Assert.Equal(1280, store.Get("Calendar")!.Width);

        File.Delete(blockedDirectory);
        Assert.True(store.Save("History", Preference(1400, 800)));
        var reopened = new WindowSizeStore(settingsPath);
        Assert.Equal(1280, reopened.Get("Calendar")!.Width);
        Assert.Equal(1400, reopened.Get("History")!.Width);
    }

    [Fact]
    public void FailedAtomicReplacementKeepsPreviousFileAndCleansTemporaryFile()
    {
        using var scope = new TestDirectory();
        var store = new WindowSizeStore(scope.Path);
        Assert.True(store.Save("MainWindow", Preference(1250, 900)));
        string previousFile = File.ReadAllText(scope.Path);
        using (var heldFile = File.Open(scope.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(store.Save("MainWindow", Preference(1500, 950)));
            Assert.Equal(1500, store.Get("MainWindow")!.Width);
        }

        Assert.Equal(previousFile, File.ReadAllText(scope.Path));
        Assert.Empty(Directory.GetFiles(scope.Root, "*.tmp"));
        Assert.True(store.Save("Calendar", Preference(1280, 820)));
        Assert.Equal(1500, new WindowSizeStore(scope.Path).Get("MainWindow")!.Width);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidDimensionsCannotReplaceAValidPreference(double invalid)
    {
        using var scope = new TestDirectory();
        var store = new WindowSizeStore(scope.Path);
        Assert.True(store.Save("MainWindow", Preference(1250, 900)));
        Assert.False(Preference(invalid, 900).IsValid);
        Assert.False(Preference(1250, invalid).IsValid);
        Assert.False(store.Save("MainWindow", Preference(invalid, 900)));
        Assert.False(store.Save("MainWindow", Preference(1250, invalid)));
        Assert.Equal(1250, new WindowSizeStore(scope.Path).Get("MainWindow")!.Width);
    }

    [Fact]
    public void InvalidKeysAndNullPreferencesAreIgnored()
    {
        using var scope = new TestDirectory();
        var store = new WindowSizeStore(scope.Path);
        foreach (string? key in new string?[] { null, "", "   ", new string('a', 513) })
        {
            Assert.Null(store.Get(key!));
            Assert.False(store.Save(key!, Preference(1250, 900)));
        }
        Assert.False(store.Save("MainWindow", null!));
        Assert.False(File.Exists(scope.Path));
        Assert.True(store.Save(new string('a', 512), Preference(1250, 900)));
    }

    private static WindowSizePreference Preference(double width, double height)
        => new() { Width = width, Height = height };

    private sealed class TestDirectory : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EdilPaintPreventivi.Tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(Root, "window-sizes.json");
        public TestDirectory() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
