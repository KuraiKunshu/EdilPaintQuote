using EdilPaintPreventibiviGen.Data;
using EdilPaintPreventibiviGen.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class MaterialOrderNotesSchemaTests
{
    [Theory]
    [InlineData(false, "nvarchar(max)")]
    [InlineData(true, "text")]
    public void OrderNotesSupportUnboundedTextAndDefaultToEmptyForExistingClients(bool postgres, string columnType)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (postgres) builder.UseNpgsql("Host=localhost;Database=unused");
        else builder.UseSqlServer("Server=localhost;Database=unused;Integrated Security=True");
        using var db = new AppDbContext(builder.Options);

        var property = db.Model.FindEntityType(typeof(QuoteEntity))!.FindProperty(nameof(QuoteEntity.MaterialOrderNotes))!;
        Assert.False(property.IsNullable);
        Assert.Equal(columnType, property.GetColumnType());
        Assert.Equal(string.Empty, property.GetDefaultValue());
    }
}
