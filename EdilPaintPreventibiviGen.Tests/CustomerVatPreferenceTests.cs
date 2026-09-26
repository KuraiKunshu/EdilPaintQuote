using System.Text.Json;
using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using EdilPaintPreventibiviGen.Data.Mappers;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class CustomerVatPreferenceTests
{
    [Theory]
    [InlineData("10%", null, false, "22%", "10%")]
    [InlineData("22%", "10%", true, "esclusa", "10%")]
    [InlineData("10%", "22%", false, "esclusa", "10%")]
    [InlineData("10%", "", true, "22%", "22%")]
    [InlineData(null, null, false, "RC 10%+22%", "RC 10%+22%")]
    [InlineData(" ESCLUSA ", null, false, "22%", "esclusa")]
    [InlineData("invalid", null, false, "22%", "22%")]
    public void CustomerOrBillingPreferenceFallsBackToTheApplicationDefault(
        string? customer, string? billing, bool hasBilling, string defaultVat, string expected)
        => Assert.Equal(expected, CustomerVatPreference.Resolve(customer, billing, hasBilling, defaultVat));

    [Fact]
    public void LegacyCustomerHasNoPreferenceAndExclusionIsAnExplicitPreference()
    {
        var legacy = JsonSerializer.Deserialize<Customer>("""{"Ragione Sociale":"Cliente"}""")!;
        Assert.Equal("", legacy.PreferredVatType);
        Assert.Equal(CustomerVatPreference.None, legacy.PreferredVatDisplay);
        legacy.PreferredVatType = "Esclusa";
        Assert.Equal("esclusa", legacy.ToEntity().ToModel().PreferredVatType);
        legacy.PreferredVatType = CustomerVatPreference.None;
        Assert.Equal("", legacy.PreferredVatType);
    }

    [Fact]
    public async Task CustomerPreferenceCanBeSavedUpdatedAndCleared()
    {
        string root = Path.Combine(Path.GetTempPath(), "customer-vat-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalJsonStoreService(root);
            var customer = new Customer { SyncId = Guid.NewGuid(), BusinessName = "Cliente", PreferredVatType = "10%" };
            await store.SaveOrUpdateCustomerAsync(customer);
            Assert.Equal("10%", Assert.Single(await store.LoadCustomersAsync()).PreferredVatType);
            customer.PreferredVatType = "RC 10%+22%";
            await store.UpdateCustomerAsync(customer.BusinessName, customer);
            Assert.Equal("RC 10%+22%", Assert.Single(await store.LoadCustomersAsync()).ToEntity().ToModel().PreferredVatType);
            customer.PreferredVatType = "";
            await store.SaveOrUpdateCustomerAsync(customer);
            Assert.Equal("", Assert.Single(await store.LoadCustomersAsync()).PreferredVatType);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CustomersWithDifferentVatPreferencesAreNotDiscardedAsDuplicates()
    {
        var result = CustomerDuplicateFilter.Compact([
            new Customer { SyncId = Guid.NewGuid(), BusinessName = "Cliente", PreferredVatType = "10%" },
            new Customer { SyncId = Guid.NewGuid(), BusinessName = "Cliente", PreferredVatType = "22%" }
        ], []);
        Assert.Equal(2, result.Kept.Count);
        Assert.Empty(result.IgnoredIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomerDatabaseFieldHasAnEmptyDefaultForBothProviders(bool postgres)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (postgres) builder.UseNpgsql("Host=localhost;Database=unused");
        else builder.UseSqlServer("Server=localhost;Database=unused;Integrated Security=True");
        using var db = new AppDbContext(builder.Options);
        var property = db.Model.FindEntityType(typeof(CustomerEntity))!.FindProperty(nameof(CustomerEntity.PreferredVatType))!;
        Assert.False(property.IsNullable);
        Assert.Equal(50, property.GetMaxLength());
        Assert.Equal("", property.GetDefaultValue());
    }
}
