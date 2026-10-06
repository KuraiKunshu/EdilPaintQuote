using System.Text;
using System.Text.Json;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Services;
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
        var employee = new EmployeeSettingsModel { FirstName = "Niccolò", LastName = "D'Amico", Abbreviation = "ND" };
        var json = JsonSerializer.Serialize(new { Employees = new[] { employee } });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var loaded = configuration.GetSection("Employees").Get<List<EmployeeSettingsModel>>() ?? [];
        var result = Assert.Single(loaded);
        Assert.Equal(employee.Id, result.Id);
        Assert.Equal(employee.FirstName, result.FirstName);
        Assert.Equal(employee.LastName, result.LastName);
        Assert.Equal(employee.Abbreviation, result.Abbreviation);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData(" mr ", "MR")]
    public void AbbreviationIsOptionalAndNormalizedWithoutChangingTheOriginal(string? value, string expected)
    {
        var employee = new EmployeeSettingsModel { FirstName = "Mario", Abbreviation = value!, Revision = 7 };
        var copy = employee.CreateValidatedCopy();
        Assert.Equal(expected, copy.Abbreviation);
        Assert.Equal(value, employee.Abbreviation);
        Assert.Equal(7, copy.Revision);
    }

    [Fact]
    public void AbbreviationHasReasonableLengthAndFirstNameRemainsMandatory()
    {
        Assert.Throws<InvalidOperationException>(() => new EmployeeSettingsModel
            { FirstName = "Mario", Abbreviation = new string('M', 25) }.CreateValidatedCopy());
        Assert.Throws<InvalidOperationException>(() => new EmployeeSettingsModel
            { Abbreviation = "MR" }.CreateValidatedCopy());
        Assert.Equal(24, new EmployeeSettingsModel
            { FirstName = "Mario", Abbreviation = new string('M', 24) }.CreateValidatedCopy().Abbreviation.Length);
    }

    [Fact]
    public void EditingOnlyAnAbbreviationIsASharedChange()
    {
        var original = new EmployeeSettingsModel { FirstName = "Mario", Revision = 3 };
        var edited = original.CreateValidatedCopy();
        edited.Abbreviation = " mr ";
        var change = Assert.Single(EmployeeChanges.Between([original], [edited]));
        var stored = new EmployeeEntity { Id = original.Id, FirstName = "Mario", Revision = 3 };
        Assert.Empty(EmployeeChanges.Apply([change], new Dictionary<Guid, EmployeeEntity> { [stored.Id] = stored }));
        Assert.Equal("MR", stored.Abbreviation);
        Assert.Equal(4, stored.Revision);
        Assert.Equal("MR", EmployeeChanges.ToModel(stored).Abbreviation);
    }

    [Fact]
    public void DuplicateAbbreviationsAreRejectedBeforeAnyRowChanges()
    {
        var existing = new EmployeeSettingsModel { FirstName = "Mario", Abbreviation = "MR", Revision = 1 };
        var another = new EmployeeSettingsModel { FirstName = "Anna", Abbreviation = " mr " };
        Assert.Throws<InvalidOperationException>(() => EmployeeChanges.Between([], [existing, another]));

        // The second PC did not see Mario when editing its original empty list.
        var row = new EmployeeEntity { Id = existing.Id, FirstName = "Mario", Abbreviation = "MR" };
        var changes = EmployeeChanges.Between([], [another]);
        Assert.Throws<InvalidOperationException>(() => EmployeeChanges.Apply(changes,
            new Dictionary<Guid, EmployeeEntity> { [row.Id] = row }));
        Assert.Equal(1, row.Revision);
        Assert.Equal("MR", row.Abbreviation);
    }

    [Fact]
    public void EmptyAliasesAndAliasesOfRemovedEmployeesDoNotBlockNewEmployees()
    {
        var deleted = new EmployeeEntity { Id = Guid.NewGuid(), FirstName = "Mario", Abbreviation = "MR", IsDeleted = true };
        var edited = new EmployeeSettingsModel[]
            { new() { FirstName = "Marco", Abbreviation = "MR" }, new() { FirstName = "Anna" }, new() { FirstName = "Luca" } };
        var added = EmployeeChanges.Apply(EmployeeChanges.Between([], edited),
            new Dictionary<Guid, EmployeeEntity> { [deleted.Id] = deleted });
        Assert.Equal(3, added.Count);
        Assert.Equal("MR", added.Single(x => x.FirstName == "Marco").Abbreviation);
    }

    [Fact]
    public void EmployeesMayExchangeAliasesWithoutLosingUnrelatedRemoteChanges()
    {
        var first = new EmployeeSettingsModel { FirstName = "Mario", Abbreviation = "MR", Revision = 1 };
        var second = new EmployeeSettingsModel { FirstName = "Luca", Abbreviation = "LB", Revision = 1 };
        var remote = new EmployeeEntity { Id = Guid.NewGuid(), FirstName = "Anna", Abbreviation = "AC", Revision = 8 };
        var rows = new[] { new EmployeeEntity { Id = first.Id, FirstName = first.FirstName, Abbreviation = "MR" },
            new EmployeeEntity { Id = second.Id, FirstName = second.FirstName, Abbreviation = "LB" }, remote }.ToDictionary(x => x.Id);
        var editedFirst = first.CreateValidatedCopy(); editedFirst.Abbreviation = "LB";
        var editedSecond = second.CreateValidatedCopy(); editedSecond.Abbreviation = "MR";
        EmployeeChanges.Apply(EmployeeChanges.Between([first, second], [editedFirst, editedSecond]), rows);
        Assert.Equal("LB", rows[first.Id].Abbreviation);
        Assert.Equal("MR", rows[second.Id].Abbreviation);
        Assert.Equal(8, remote.Revision);
        Assert.Equal("AC", remote.Abbreviation);
    }

    [Fact]
    public void LegacyImportRetainsUsableAliasesButDoesNotDuplicateActiveAliases()
    {
        var stored = new EmployeeEntity { Id = Guid.NewGuid(), FirstName = "Mario", Abbreviation = "MR" };
        var imports = EmployeeChanges.LegacyImports([
            new() { FirstName = "Marco", Abbreviation = " mr " },
            new() { FirstName = "Luca", Abbreviation = " lb " }
        ], [stored]);
        Assert.Equal("", imports.Single(x => x.FirstName == "Marco").Abbreviation);
        Assert.Equal("LB", imports.Single(x => x.FirstName == "Luca").Abbreviation);
        Assert.All(imports, x => Assert.False(x.IsDeleted));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingEmployeeSchemasAddAliasAndUniqueActiveIndex(bool postgres)
    {
        string schema = SqlDataService.BuildEmployeeSchemaSql(postgres);
        Assert.Contains(postgres ? "ADD COLUMN IF NOT EXISTS \"Abbreviation\"" : "COL_LENGTH(N'dbo.Employees', N'Abbreviation')", schema);
        Assert.Contains("CREATE UNIQUE INDEX", schema);
        Assert.Contains("IX_Employees_Abbreviation", schema);
        Assert.Contains(postgres ? "\"IsDeleted\" = false" : "[IsDeleted] = 0", schema);
    }
}
