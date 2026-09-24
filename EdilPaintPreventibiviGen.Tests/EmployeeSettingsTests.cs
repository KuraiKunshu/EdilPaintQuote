using System.Text;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class EmployeeSettingsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RequiresFirstNameEvenWithLastName(string? firstName)
    {
        var employee = new EmployeeSettingsModel { FirstName = firstName!, LastName = "Rossi" };
        Assert.Throws<InvalidOperationException>(() => employee.CreateValidatedCopy());
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData(" Rossi ", "Rossi")]
    public void ValidatedCopyTrimsNamesWithoutChangingOriginal(string? lastName, string expected)
    {
        var employee = new EmployeeSettingsModel { FirstName = " Mario ", LastName = lastName! };
        var copy = employee.CreateValidatedCopy();
        Assert.Equal(employee.Id, copy.Id);
        Assert.Equal("Mario", copy.FirstName);
        Assert.Equal(expected, copy.LastName);
        Assert.Equal(" Mario ", employee.FirstName);
        Assert.NotSame(employee, copy);
    }

    [Fact]
    public void EmployeeListSurvivesSettingsJsonRoundTrip()
    {
        var employee = new EmployeeSettingsModel { FirstName = "Niccolò", LastName = "D'Amico" };
        var json = JsonSerializer.Serialize(new { Employees = new[] { employee } });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var loaded = configuration.GetSection("Employees").Get<List<EmployeeSettingsModel>>() ?? [];
        var result = Assert.Single(loaded);
        Assert.Equal(employee.Id, result.Id);
        Assert.Equal(employee.FirstName, result.FirstName);
        Assert.Equal(employee.LastName, result.LastName);
    }
}
