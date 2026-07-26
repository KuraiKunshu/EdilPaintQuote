using EdilPaintPreventibiviGen.Android.Models;
using Npgsql;

namespace EdilPaintPreventibiviGen.Android.Services;

public sealed class QuoteReadService
{
    public async Task TestConnectionAsync(string connectionString)
    {
        await ExecuteWithRetryAsync(async cancellationToken =>
        {
            await using var connection = new NpgsqlConnection(NormalizeConnectionString(connectionString));
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("select 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
        });
    }

    public Task<IReadOnlyList<QuoteSummary>> GetSummariesAsync(
        string connectionString,
        string search,
        QuoteStatus? status,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithRetryAsync(token => GetSummariesOnceAsync(connectionString, search, status, token), cancellationToken);
    }

    private static async Task<IReadOnlyList<QuoteSummary>> GetSummariesOnceAsync(
        string connectionString,
        string search,
        QuoteStatus? status,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(NormalizeConnectionString(connectionString));
        await connection.OpenAsync(cancellationToken);

        var where = new List<string> { "not q.\"IsDeleted\"" };
        await using var command = connection.CreateCommand();

        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(q.\"QuoteNumber\" ilike @search or c.\"BusinessName\" ilike @search or r.\"BusinessName\" ilike @search or q.\"Notes\" ilike @search)");
            command.Parameters.AddWithValue("search", $"%{search.Trim()}%");
        }

        if (status.HasValue)
        {
            where.Add("q.\"Status\" = @status");
            command.Parameters.AddWithValue("status", (int)status.Value);
        }

        command.CommandText = $"""
            select
                q."QuoteNumber",
                q."Date",
                coalesce(c."BusinessName", '') as "CustomerName",
                coalesce(r."BusinessName", '') as "ReferenceName",
                q."Total",
                q."IvaType",
                q."Status",
                q."SentAtUtc",
                q."Notes"
            from "Quotes" q
            left join "Customers" c on c."Id" = q."CustomerId"
            left join "Customers" r on r."Id" = q."ReferenceCustomerId"
            where {string.Join(" and ", where)}
            order by q."Date" desc
            limit 120;
            """;

        var quotes = new List<QuoteSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            quotes.Add(new QuoteSummary
            {
                QuoteNumber = reader.GetString(0),
                Date = reader.GetDateTime(1),
                CustomerName = reader.GetString(2),
                ReferenceName = reader.GetString(3),
                Total = reader.GetDouble(4),
                IvaType = reader.GetString(5),
                Status = (QuoteStatus)reader.GetInt32(6),
                SentAtUtc = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                HasNotes = !reader.IsDBNull(8) && !string.IsNullOrWhiteSpace(reader.GetString(8))
            });
        }

        return quotes;
    }

    public Task<QuoteDetail> GetDetailAsync(
        string connectionString,
        string quoteNumber,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithRetryAsync(token => GetDetailOnceAsync(connectionString, quoteNumber, token), cancellationToken);
    }

    private static async Task<QuoteDetail> GetDetailOnceAsync(
        string connectionString,
        string quoteNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(NormalizeConnectionString(connectionString));
        await connection.OpenAsync(cancellationToken);

        QuoteDetail detail;
        int quoteId;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                select
                    q."Id",
                    q."QuoteNumber",
                    q."Date",
                    coalesce(c."BusinessName", '') as "CustomerName",
                    coalesce(r."BusinessName", '') as "ReferenceName",
                    q."PaymentTerms",
                    q."IvaType",
                    q."Notes",
                    q."Imponibile",
                    q."Total",
                    q."MaterialDiscount",
                    q."LaborDiscount",
                    q."Status",
                    q."SentAtUtc",
                    q."SentRecipient",
                    q."LastModifiedByDevice"
                from "Quotes" q
                left join "Customers" c on c."Id" = q."CustomerId"
                left join "Customers" r on r."Id" = q."ReferenceCustomerId"
                where not q."IsDeleted" and q."QuoteNumber" = @quoteNumber
                limit 1;
                """;
            command.Parameters.AddWithValue("quoteNumber", quoteNumber);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException($"Preventivo {quoteNumber} non trovato.");

            quoteId = reader.GetInt32(0);
            detail = new QuoteDetail
            {
                QuoteNumber = reader.GetString(1),
                Date = reader.GetDateTime(2),
                CustomerName = reader.GetString(3),
                ReferenceName = reader.GetString(4),
                PaymentTerms = reader.GetString(5),
                IvaType = reader.GetString(6),
                Notes = reader.GetString(7),
                Imponibile = reader.GetDouble(8),
                Total = reader.GetDouble(9),
                MaterialDiscount = reader.GetDouble(10),
                LaborDiscount = reader.GetDouble(11),
                Status = (QuoteStatus)reader.GetInt32(12),
                SentAtUtc = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
                SentRecipient = reader.GetString(14),
                LastModifiedByDevice = reader.GetString(15)
            };
        }

        detail.Materials.AddRange(await LoadLinesAsync(connection, quoteId, "QuoteMaterials", cancellationToken));
        detail.Labors.AddRange(await LoadLinesAsync(connection, quoteId, "QuoteLabors", cancellationToken));
        return detail;
    }

    private static async Task<List<QuoteLine>> LoadLinesAsync(
        NpgsqlConnection connection,
        int quoteId,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select "Name", "Description", "UnitPrice", "Quantity", "Discount"
            from "{tableName}"
            where "QuoteId" = @quoteId
            order by "SortOrder", "Id";
            """;
        command.Parameters.AddWithValue("quoteId", quoteId);

        var lines = new List<QuoteLine>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            double unitPrice = reader.GetDouble(2);
            int quantity = reader.GetInt32(3);
            double discount = reader.GetDouble(4);
            double total = unitPrice * quantity * (1 - Math.Clamp(discount, 0, 100) / 100);

            lines.Add(new QuoteLine
            {
                Name = reader.GetString(0),
                Description = reader.GetString(1),
                UnitPrice = unitPrice,
                Quantity = quantity,
                Discount = discount,
                Total = total
            });
        }

        return lines;
    }

    private static string NormalizeConnectionString(string connectionString)
    {
        connectionString = connectionString.Trim();
        if (connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertUriConnectionString(connectionString);
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Pooling = false;
        if (builder.SslMode == SslMode.Prefer)
            builder.SslMode = SslMode.Require;
        if (builder.Timeout <= 0)
            builder.Timeout = 15;
        if (builder.CommandTimeout <= 0)
            builder.CommandTimeout = 15;

        return builder.ConnectionString;
    }

    private static string ConvertUriConnectionString(string connectionString)
    {
        var uri = new Uri(connectionString);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            SslMode = SslMode.Require,
            Pooling = false,
            Timeout = 15,
            CommandTimeout = 15
        };

        if (uri.Port > 0)
            builder.Port = uri.Port;

        string userInfo = uri.UserInfo;
        int separator = userInfo.IndexOf(':');
        if (separator >= 0)
        {
            builder.Username = Uri.UnescapeDataString(userInfo[..separator]);
            builder.Password = Uri.UnescapeDataString(userInfo[(separator + 1)..]);
        }
        else if (!string.IsNullOrWhiteSpace(userInfo))
        {
            builder.Username = Uri.UnescapeDataString(userInfo);
        }

        foreach (var (name, value) in ParseQuery(uri.Query))
        {
            if (name.Equals("sslmode", StringComparison.OrdinalIgnoreCase) &&
                value.Equals("disable", StringComparison.OrdinalIgnoreCase))
            {
                builder.SslMode = SslMode.Disable;
            }

            if (name.Equals("channel_binding", StringComparison.OrdinalIgnoreCase))
            {
                TrySetConnectionStringValue(builder, "Channel Binding", NormalizeRequirePreferDisable(value));
            }
        }

        return builder.ConnectionString;
    }

    private static async Task ExecuteWithRetryAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async token =>
        {
            await operation(token);
            return true;
        }, cancellationToken);
    }

    private static async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 3;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsRetryable(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken);
            }
        }
    }

    private static bool IsRetryable(Exception ex)
    {
        if (ex is NpgsqlException)
            return true;

        string message = ex.GetBaseException().Message;
        return message.Contains("connection abort", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("connection reset", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<(string Name, string Value)> ParseQuery(string query)
    {
        query = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
            yield break;

        foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pieces = part.Split('=', 2);
            string name = Uri.UnescapeDataString(pieces[0].Replace("+", " "));
            string value = pieces.Length == 2
                ? Uri.UnescapeDataString(pieces[1].Replace("+", " "))
                : string.Empty;

            yield return (name, value);
        }
    }

    private static string NormalizeRequirePreferDisable(string value) => value.ToLowerInvariant() switch
    {
        "require" => "Require",
        "prefer" => "Prefer",
        "disable" => "Disable",
        _ => value
    };

    private static void TrySetConnectionStringValue(
        NpgsqlConnectionStringBuilder builder,
        string key,
        string value)
    {
        try
        {
            builder[key] = value;
        }
        catch
        {
            // Older provider builds may not support optional Neon parameters.
        }
    }
}
