using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class CustomerDuplicateFilterTests
{
    [Fact]
    public void ExactUnprotectedDuplicatesKeepOneDeterministicId()
    {
        Guid lowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid higherId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var result = CustomerDuplicateFilter.Compact([
            CreateCustomer(higherId),
            CreateCustomer(lowerId, businessName: "  cliente prova ", email: "INFO@EXAMPLE.IT")
        ], []);

        var kept = Assert.Single(result.Kept);
        Assert.Equal(lowerId, kept.SyncId);
        Assert.Contains(higherId, result.IgnoredIds);
        Assert.DoesNotContain(lowerId, result.IgnoredIds);
    }

    [Fact]
    public void HomonymsWithDifferentVisibleContentAreBothKept()
    {
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        var result = CustomerDuplicateFilter.Compact([
            CreateCustomer(firstId, address: "Via Roma 1"),
            CreateCustomer(secondId, address: "via Roma 1")
        ], []);

        Assert.Equal(2, result.Kept.Count);
        Assert.Empty(result.IgnoredIds);
    }

    [Fact]
    public void ProtectedDuplicateIsKeptInsteadOfDeterministicDefault()
    {
        Guid lowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid protectedId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var result = CustomerDuplicateFilter.Compact([
            CreateCustomer(lowerId),
            CreateCustomer(protectedId)
        ], [protectedId]);

        Assert.Equal(protectedId, Assert.Single(result.Kept).SyncId);
        Assert.Contains(lowerId, result.IgnoredIds);
    }

    [Fact]
    public void MultipleProtectedDuplicatesAreAllKept()
    {
        Guid firstProtectedId = Guid.NewGuid();
        Guid secondProtectedId = Guid.NewGuid();
        Guid unprotectedId = Guid.NewGuid();
        var result = CustomerDuplicateFilter.Compact([
            CreateCustomer(firstProtectedId),
            CreateCustomer(unprotectedId),
            CreateCustomer(secondProtectedId)
        ], [firstProtectedId, secondProtectedId]);

        Assert.Equal(2, result.Kept.Count);
        Assert.Contains(result.Kept, customer => customer.SyncId == firstProtectedId);
        Assert.Contains(result.Kept, customer => customer.SyncId == secondProtectedId);
        Assert.Contains(unprotectedId, result.IgnoredIds);
    }

    [Fact]
    public void PendingLocalCustomerIdCanBeProtectedBeforeCompaction()
    {
        Guid databaseId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid pendingId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var localCustomers = new[]
        {
            CreateCustomer(pendingId, hasPendingDatabaseWrite: true)
        };
        var protectedIds = localCustomers
            .Where(customer => customer.HasPendingDatabaseWrite)
            .Select(customer => customer.SyncId);

        var result = CustomerDuplicateFilter.Compact([
            CreateCustomer(databaseId),
            CreateCustomer(pendingId)
        ], protectedIds);

        Assert.Equal(pendingId, Assert.Single(result.Kept).SyncId);
        Assert.Contains(databaseId, result.IgnoredIds);
    }

    [Fact]
    public void LegacyEmptyIdDoesNotReplaceStableCanonicalId()
    {
        Guid canonicalId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid duplicateId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var result = CustomerDuplicateFilter.Compact([
            CreateCustomer(Guid.Empty),
            CreateCustomer(duplicateId),
            CreateCustomer(canonicalId)
        ], []);

        Assert.Contains(result.Kept, customer => customer.SyncId == Guid.Empty);
        Assert.Contains(result.Kept, customer => customer.SyncId == canonicalId);
        Assert.DoesNotContain(result.Kept, customer => customer.SyncId == duplicateId);
        Assert.Contains(duplicateId, result.IgnoredIds);
    }

    private static Customer CreateCustomer(
        Guid syncId,
        string businessName = "Cliente Prova",
        string address = "Via Roma 1",
        string email = "info@example.it",
        bool hasPendingDatabaseWrite = false) =>
        new()
        {
            SyncId = syncId,
            BusinessName = businessName,
            Address = address,
            Email = email,
            Phone = "0123456789",
            MaterialDiscount = 10,
            LaborDiscount = 5,
            SupplierDiscount = 15,
            IsSupplier = true,
            HasPendingDatabaseWrite = hasPendingDatabaseWrite
        };
}
