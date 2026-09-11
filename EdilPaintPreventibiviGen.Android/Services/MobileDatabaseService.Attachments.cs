using Npgsql;
using EdilPaintPreventibiviGen.Android.Models;

namespace EdilPaintPreventibiviGen.Android.Services;

public sealed record MobileAttachment(int Id, string FileName, string ContentType, long Size, DateTime ImportedAtUtc);

public sealed partial class MobileDatabaseService
{
    public Task<IReadOnlyList<MobileAttachment>> GetAttachmentsAsync(string connectionString, int quoteId) =>
        ExecuteReadWithRetryAsync<IReadOnlyList<MobileAttachment>>(async token =>
        {
            await using var connection = await OpenConnectionAsync(connectionString, token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                select a."Id", a."FileName", a."ContentType", octet_length(a."Content"), a."ImportedAtUtc"
                from "QuoteAttachments" a join "Quotes" q on q."Id" = a."QuoteId"
                where a."QuoteId" = @id and not q."IsDeleted" order by a."ImportedAtUtc" desc, a."Id";
                """;
            command.Parameters.AddWithValue("id", quoteId);
            var items = new List<MobileAttachment>();
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) items.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetDateTime(4)));
            return items;
        });

    public Task<byte[]> GetAttachmentContentAsync(string connectionString, int quoteId, int attachmentId) =>
        ExecuteReadWithRetryAsync(async token =>
        {
            await using var connection = await OpenConnectionAsync(connectionString, token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                select a."Content" from "QuoteAttachments" a join "Quotes" q on q."Id" = a."QuoteId"
                where a."Id" = @id and a."QuoteId" = @quoteId and not q."IsDeleted"
                and octet_length(a."Content") <= 20971520;
                """;
            command.Parameters.AddWithValue("id", attachmentId);
            command.Parameters.AddWithValue("quoteId", quoteId);
            return await command.ExecuteScalarAsync(token) as byte[] ?? throw new InvalidOperationException("Allegato non disponibile o maggiore di 20 MB.");
        });

    public Task<long> AddAttachmentAsync(string connectionString, QuoteDetail quote, long revision, string fileName, string contentType, byte[] content)
    {
        if (content.Length == 0 || content.Length > 20 * 1024 * 1024) throw new InvalidOperationException("L'allegato deve essere compreso tra 1 byte e 20 MB.");
        return UpdateQuoteOperationalAsync(connectionString, quote.QuoteNumber, revision, "\"Status\" = \"Status\"", _ => { },
            "allegato", "Allegato aggiunto: " + Path.GetFileName(fileName), CancellationToken.None,
            async (connection, transaction, quoteId, token) =>
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    insert into "QuoteAttachments" ("QuoteId", "FileName", "ContentType", "Content", "ImportedAtUtc")
                    values (@quoteId, @name, @type, @content, @date);
                    """;
                command.Parameters.AddWithValue("quoteId", quoteId);
                command.Parameters.AddWithValue("name", Path.GetFileName(fileName));
                command.Parameters.AddWithValue("type", contentType);
                command.Parameters.AddWithValue("content", content);
                command.Parameters.AddWithValue("date", DateTime.UtcNow);
                await command.ExecuteNonQueryAsync(token);
            });
    }

    public Task<long> DeleteAttachmentAsync(string connectionString, QuoteDetail quote, long revision, MobileAttachment item) =>
        UpdateQuoteOperationalAsync(connectionString, quote.QuoteNumber, revision, "\"Status\" = \"Status\"", _ => { },
            "allegato", "Allegato rimosso: " + item.FileName, CancellationToken.None,
            async (connection, transaction, quoteId, token) =>
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "delete from \"QuoteAttachments\" where \"Id\" = @id and \"QuoteId\" = @quoteId;";
                command.Parameters.AddWithValue("id", item.Id);
                command.Parameters.AddWithValue("quoteId", quoteId);
                if (await command.ExecuteNonQueryAsync(token) != 1) throw new DatabaseWriteConflictException("Allegato gia' rimosso.");
            });
}
