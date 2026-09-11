using System.Data;
using System.Text.Json;
using EdilPaintPreventibiviGen.Android.Models;
using Npgsql;
using NpgsqlTypes;

namespace EdilPaintPreventibiviGen.Android.Services;

public sealed partial class MobileDatabaseService
{
    public Task<DashboardSnapshot> GetDashboardAsync(
        string connectionString,
        CancellationToken cancellationToken = default) =>
        ExecuteReadWithRetryAsync<DashboardSnapshot>(async token =>
        {
            await using var connection = await OpenConnectionAsync(connectionString, token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                select
                    count(*) filter (where not "IsDeleted"),
                    count(*) filter (where not "IsDeleted" and "Status" = @draft),
                    count(*) filter (
                        where not "IsDeleted"
                          and "SentAtUtc" is not null
                          and "Status" not in (@confirmed, @finished, @rejected, @archived)),
                    count(*) filter (where not "IsDeleted" and "Status" = @confirmed),
                    count(*) filter (
                        where not "IsDeleted"
                          and ("MaterialsOrderedByCustomer" or "SupplierName" <> '' or "MaterialStatus" <> '')
                          and upper(coalesce("MaterialStatus", '')) not in ('CONSEGNATO', 'NON DISPONIBILE')),
                    coalesce(sum("Total") filter (
                        where not "IsDeleted" and "Status" in (@confirmed, @finished)), 0),
                    (select count(*) from "Customers" where not "IsDeleted" and not "IsSupplier")
                from "Quotes";
                """;
            command.Parameters.AddWithValue("draft", (int)QuoteStatus.Bozza);
            command.Parameters.AddWithValue("confirmed", (int)QuoteStatus.Confermato);
            command.Parameters.AddWithValue("finished", (int)QuoteStatus.Finito);
            command.Parameters.AddWithValue("rejected", (int)QuoteStatus.Rifiutato);
            command.Parameters.AddWithValue("archived", (int)QuoteStatus.Archiviato);

            await using var reader = await command.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token))
                return new DashboardSnapshot();

            var snapshot = new DashboardSnapshot
            {
                TotalQuotes = Convert.ToInt32(reader.GetInt64(0)),
                DraftQuotes = Convert.ToInt32(reader.GetInt64(1)),
                SentOpenQuotes = Convert.ToInt32(reader.GetInt64(2)),
                ConfirmedQuotes = Convert.ToInt32(reader.GetInt64(3)),
                PendingOrders = Convert.ToInt32(reader.GetInt64(4)),
                ConfirmedValue = reader.GetDouble(5),
                Customers = Convert.ToInt32(reader.GetInt64(6))
            };
            await reader.CloseAsync();

            IReadOnlyList<QuoteSummary> recent = await GetQuoteSummariesOnceAsync(
                connectionString,
                string.Empty,
                null,
                token);
            return new DashboardSnapshot
            {
                TotalQuotes = snapshot.TotalQuotes,
                DraftQuotes = snapshot.DraftQuotes,
                SentOpenQuotes = snapshot.SentOpenQuotes,
                ConfirmedQuotes = snapshot.ConfirmedQuotes,
                PendingOrders = snapshot.PendingOrders,
                ConfirmedValue = snapshot.ConfirmedValue,
                Customers = snapshot.Customers,
                RecentQuotes = recent.Take(5).ToArray()
            };
        }, cancellationToken);

    public Task<IReadOnlyList<SupplierOrderSummary>> GetSupplierOrdersAsync(
        string connectionString,
        string search,
        string? status,
        CancellationToken cancellationToken = default) =>
        ExecuteReadWithRetryAsync<IReadOnlyList<SupplierOrderSummary>>(async token =>
        {
            await using var connection = await OpenConnectionAsync(connectionString, token);
            await using var command = connection.CreateCommand();
            var filters = new List<string>
            {
                "not q.\"IsDeleted\"",
                "(q.\"MaterialsOrderedByCustomer\" or (q.\"Status\" = @confirmed and (q.\"SupplierName\" <> '' or q.\"MaterialOrderDate\" is not null or q.\"ExpectedDeliveryDate\" is not null or q.\"MaterialStatus\" <> '')))"
            };
            command.Parameters.AddWithValue("confirmed", (int)QuoteStatus.Confermato);

            if (!string.IsNullOrWhiteSpace(search))
            {
                filters.Add("(q.\"QuoteNumber\" ilike @search or c.\"BusinessName\" ilike @search or r.\"BusinessName\" ilike @search or q.\"SiteName\" ilike @search or q.\"SupplierName\" ilike @search)");
                command.Parameters.AddWithValue("search", $"%{search.Trim()}%");
            }

            if (!string.IsNullOrWhiteSpace(status) && !status.Equals("TUTTI", StringComparison.OrdinalIgnoreCase))
            {
                filters.Add("upper(coalesce(nullif(q.\"MaterialStatus\", ''), 'DA ORDINARE')) = @orderStatus");
                command.Parameters.AddWithValue("orderStatus", SupplierOrderStatusOptions.Normalize(status));
            }

            command.CommandText = $"""
                select q."Id", q."QuoteNumber", q."Date",
                       coalesce(c."BusinessName", ''), coalesce(r."BusinessName", ''),
                       q."SiteName", q."SupplierName", q."MaterialsOrderedByCustomer",
                       q."MaterialOrderDate", q."ExpectedDeliveryDate", q."MaterialStatus", q."Revision"
                from "Quotes" q
                left join "Customers" c on c."Id" = q."CustomerId"
                left join "Customers" r on r."Id" = q."ReferenceCustomerId"
                where {string.Join(" and ", filters)}
                order by q."MaterialOrderDate" is null, q."MaterialOrderDate" desc, q."Date" desc
                ;
                """;

            var orders = new List<SupplierOrderSummary>();
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                orders.Add(new SupplierOrderSummary
                {
                    QuoteId = reader.GetInt32(0),
                    QuoteNumber = reader.GetString(1),
                    Date = reader.GetDateTime(2),
                    CustomerName = reader.GetString(3),
                    ReferenceName = reader.GetString(4),
                    SiteName = reader.GetString(5),
                    SupplierName = reader.GetString(6),
                    MaterialsOrderedByCustomer = reader.GetBoolean(7),
                    MaterialOrderDate = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                    ExpectedDeliveryDate = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                    MaterialStatus = reader.GetString(10),
                    Revision = reader.GetInt64(11)
                });
            }

            return orders.ToArray();
        }, cancellationToken);

    public Task<IReadOnlyList<SupplierRecord>> GetSuppliersAsync(
        string connectionString,
        CancellationToken cancellationToken = default) =>
        ExecuteReadWithRetryAsync<IReadOnlyList<SupplierRecord>>(async token =>
        {
            await using var connection = await OpenConnectionAsync(connectionString, token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                select "Id", "BusinessName", "Email", "SupplierDiscount"
                from "Customers"
                where not "IsDeleted" and "IsSupplier"
                order by "BusinessName";
                """;
            var suppliers = new List<SupplierRecord>();
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                suppliers.Add(new SupplierRecord
                {
                    Id = reader.GetInt32(0),
                    BusinessName = reader.GetString(1),
                    Email = reader.GetString(2),
                    SupplierDiscount = reader.GetDouble(3)
                });
            }
            return suppliers.ToArray();
        }, cancellationToken);

    public Task<CompanyContact> GetCompanyContactAsync(
        string connectionString,
        CancellationToken cancellationToken = default) =>
        ExecuteReadWithRetryAsync(async token =>
        {
            await using var connection = await OpenConnectionAsync(connectionString, token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                select coalesce("Nome", ''), coalesce("Email", ''), coalesce("Indirizzo", ''), coalesce("Piva", '')
                from "CompanySettings"
                order by "Id"
                limit 1;
                """;
            await using var reader = await command.ExecuteReaderAsync(token);
            return await reader.ReadAsync(token)
                ? new CompanyContact(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3))
                : new CompanyContact("EdilPaint", string.Empty);
        }, cancellationToken);

    public Task<long> UpdateSupplierOrderAsync(
        string connectionString,
        SupplierOrderUpdate update,
        CancellationToken cancellationToken = default)
    {
        string status = SupplierOrderStatusOptions.Normalize(update.MaterialStatus);
        DateTime? orderDate = update.MaterialOrderDate;
        if (status != "DA ORDINARE" && !orderDate.HasValue)
            orderDate = DateTime.Today;

        return UpdateQuoteOperationalAsync(
            connectionString,
            update.QuoteNumber,
            update.ExpectedRevision,
            """
            "SupplierName" = @supplierName,
            "MaterialsOrderedByCustomer" = @orderedByCustomer,
            "MaterialOrderDate" = @orderDate,
            "ExpectedDeliveryDate" = @deliveryDate,
            "MaterialStatus" = @materialStatus
            """,
            command =>
            {
                command.Parameters.AddWithValue("supplierName", update.SupplierName.Trim());
                command.Parameters.AddWithValue("orderedByCustomer", update.MaterialsOrderedByCustomer);
                AddNullableDateTime(command, "orderDate", orderDate);
                AddNullableDateTime(command, "deliveryDate", update.ExpectedDeliveryDate);
                command.Parameters.AddWithValue("materialStatus", status);
            },
            "fornitori",
            $"Dati ordine aggiornati: {status}",
            cancellationToken);
    }

    public Task<long> UpdateQuoteStatusAsync(
        string connectionString,
        string quoteNumber,
        QuoteStatus status,
        long? expectedRevision = null,
        CancellationToken cancellationToken = default) =>
        UpdateQuoteOperationalAsync(
            connectionString,
            quoteNumber,
            expectedRevision,
            "\"Status\" = @status",
            command => command.Parameters.AddWithValue("status", (int)status),
            "stato",
            $"Stato aggiornato: {status}",
            cancellationToken);

    public Task<long> RegisterQuoteSentAsync(
        string connectionString,
        string quoteNumber,
        string recipient,
        long? expectedRevision = null,
        CancellationToken cancellationToken = default) =>
        UpdateQuoteOperationalAsync(
            connectionString,
            quoteNumber,
            expectedRevision,
            """
            "Status" = case when "Status" in (0, 5, 6, 7) then @status else "Status" end,
            "SentAtUtc" = @sentAt,
            "SentMethod" = @method,
            "SentRecipient" = @recipient,
            "SentByDevice" = @device
            """,
            command =>
            {
                command.Parameters.AddWithValue("status", (int)QuoteStatus.Spedito);
                command.Parameters.AddWithValue("sentAt", DateTime.UtcNow);
                command.Parameters.AddWithValue("method", "Email Android");
                command.Parameters.AddWithValue("recipient", recipient.Trim());
            },
            "invio",
            "Preventivo inviato tramite Email Android",
            cancellationToken);

    public Task<long> RegisterQuoteReminderAsync(
        string connectionString,
        string quoteNumber,
        long? expectedRevision = null,
        CancellationToken cancellationToken = default) =>
        UpdateQuoteOperationalAsync(
            connectionString,
            quoteNumber,
            expectedRevision,
            """
            "Status" = case when "Status" in (0, 5, 6, 7) then @status else "Status" end,
            "LastReminderAtUtc" = @reminderAt,
            "ReminderCount" = "ReminderCount" + 1,
            "LastReminderByDevice" = @device
            """,
            command =>
            {
                command.Parameters.AddWithValue("status", (int)QuoteStatus.Spedito);
                command.Parameters.AddWithValue("reminderAt", DateTime.UtcNow);
            },
            "sollecito",
            "Sollecito registrato",
            cancellationToken);

    public Task<long> SaveRealProfitAsync(
        string connectionString,
        string quoteNumber,
        RealProfitSnapshot snapshot,
        long? expectedRevision = null,
        CancellationToken cancellationToken = default)
    {
        snapshot.CalculatedAtUtc = DateTime.UtcNow;
        snapshot.CalculatedByDevice = GetDeviceName();
        string json = JsonSerializer.Serialize(snapshot);
        return UpdateQuoteOperationalAsync(
            connectionString,
            quoteNumber,
            expectedRevision,
            "\"RealProfitJson\" = @realProfitJson",
            command => command.Parameters.AddWithValue("realProfitJson", json),
            "guadagno-reale",
            $"Guadagno reale ricalcolato: {snapshot.Result.Profit:N2} euro",
            cancellationToken);
    }

    public Task<long> RegisterSupplierOrderSentAsync(string connectionString, string quoteNumber, long revision) =>
        UpdateQuoteOperationalAsync(connectionString, quoteNumber, revision,
            """
            "MaterialStatus" = case when upper(coalesce("MaterialStatus", '')) in ('', 'DA ORDINARE') then 'ORDINATO' else "MaterialStatus" end,
            "MaterialOrderDate" = coalesce("MaterialOrderDate", @orderDate)
            """,
            command => command.Parameters.AddWithValue("orderDate", EnsureUtc(DateTime.Today)),
            "ordine-inviato", "Ordine materiali inviato tramite Email Android", CancellationToken.None);

    public Task<long> DeleteQuoteAsync(
        string connectionString,
        string quoteNumber,
        long? expectedRevision = null,
        CancellationToken cancellationToken = default) =>
        UpdateQuoteOperationalAsync(
            connectionString,
            quoteNumber,
            expectedRevision,
            "\"IsDeleted\" = true",
            _ => { },
            "eliminazione",
            "Preventivo eliminato",
            cancellationToken);

    public async Task DeleteCustomerAsync(
        string connectionString,
        CustomerRecord customer,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            update "Customers"
            set "IsDeleted" = true, "LastModifiedUtc" = @savedAtUtc
            where "SyncId" = @syncId
              and "LastModifiedUtc" = @expectedLastModifiedUtc
              and not "IsDeleted"
              and not "IsSupplier";
            """;
        command.Parameters.AddWithValue("savedAtUtc", DateTime.UtcNow);
        command.Parameters.AddWithValue("syncId", customer.SyncId);
        command.Parameters.AddWithValue("expectedLastModifiedUtc", EnsureUtc(customer.LastModifiedUtc));
        int affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
            throw new DatabaseWriteConflictException("Il cliente è stato modificato oppure è già stato eliminato.");
    }

    public async Task<CatalogItem> SaveCatalogItemAsync(
        string connectionString,
        QuoteLineKind kind,
        CatalogItem item,
        CancellationToken cancellationToken = default,
        CatalogItem? original = null)
    {
        item.Name = item.Name.Trim();
        item.Description = item.Description.Trim();
        if (string.IsNullOrWhiteSpace(item.Name))
            throw new InvalidOperationException("Inserisci il nome della voce.");
        if (item.Name.Length > 250) throw new InvalidOperationException("Nome voce: massimo 250 caratteri.");
        if (!double.IsFinite(item.UnitPrice) || item.UnitPrice < 0)
            throw new InvalidOperationException("Il prezzo non può essere negativo.");

        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("name", item.Name);
        command.Parameters.AddWithValue("description", item.Description);
        command.Parameters.AddWithValue("unitPrice", item.UnitPrice);

        if (kind == QuoteLineKind.Material)
        {
            command.Parameters.AddWithValue("isSignificant", item.IsSignificant);
            command.Parameters.AddWithValue("isCompanyMaterial", item.IsCompanyMaterial);
            command.CommandText = item.Id == 0
                ? """
                  insert into "PersonalMaterials" ("Name", "Description", "UnitPrice", "IsSignificant", "IsCompanyMaterial")
                  values (@name, @description, @unitPrice, @isSignificant, @isCompanyMaterial)
                  returning "Id";
                  """
                : """
                  update "PersonalMaterials"
                  set "Name" = @name, "Description" = @description, "UnitPrice" = @unitPrice,
                      "IsSignificant" = @isSignificant, "IsCompanyMaterial" = @isCompanyMaterial
                  where "Id" = @id
                  returning "Id";
                  """;
        }
        else
        {
            command.CommandText = item.Id == 0
                ? """
                  insert into "LaborCatalog" ("Name", "Description", "UnitPrice")
                  values (@name, @description, @unitPrice)
                  returning "Id";
                  """
                : """
                  update "LaborCatalog"
                  set "Name" = @name, "Description" = @description, "UnitPrice" = @unitPrice
                  where "Id" = @id
                  returning "Id";
                  """;
        }

        if (item.Id != 0)
        {
            command.Parameters.AddWithValue("id", item.Id);
            if (original == null) throw new InvalidOperationException("Riapri la voce prima di modificarla.");
            AddCatalogConcurrency(command, kind, original);
        }
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
            throw new DatabaseWriteConflictException("La voce di catalogo e' stata modificata o eliminata da un altro dispositivo. Riaprila prima di salvare.");
        item.Id = Convert.ToInt32(result);
        return item;
    }

    public async Task DeleteCatalogItemAsync(
        string connectionString,
        QuoteLineKind kind,
        int id,
        CancellationToken cancellationToken = default,
        CatalogItem? original = null)
    {
        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = kind == QuoteLineKind.Material
            ? "delete from \"PersonalMaterials\" where \"Id\" = @id;"
            : "delete from \"LaborCatalog\" where \"Id\" = @id;";
        command.Parameters.AddWithValue("id", id);
        if (original == null) throw new InvalidOperationException("Riapri la voce prima di eliminarla.");
        AddCatalogConcurrency(command, kind, original);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new DatabaseWriteConflictException("La voce e' cambiata. Aggiorna il catalogo e riprova.");
    }

    private static void AddCatalogConcurrency(NpgsqlCommand command, QuoteLineKind kind, CatalogItem original)
    {
        string condition = " and \"Name\" = @oldName and \"Description\" = @oldDescription and \"UnitPrice\" = @oldPrice";
        command.Parameters.AddWithValue("oldName", original.Name);
        command.Parameters.AddWithValue("oldDescription", original.Description);
        command.Parameters.AddWithValue("oldPrice", original.UnitPrice);
        if (kind == QuoteLineKind.Material)
        {
            condition += " and \"IsSignificant\" = @oldSignificant and \"IsCompanyMaterial\" = @oldCompany";
            command.Parameters.AddWithValue("oldSignificant", original.IsSignificant);
            command.Parameters.AddWithValue("oldCompany", original.IsCompanyMaterial);
        }
        command.CommandText = command.CommandText.Replace("where \"Id\" = @id", "where \"Id\" = @id" + condition, StringComparison.Ordinal);
    }

    private static async Task<long> UpdateQuoteOperationalAsync(
        string connectionString,
        string quoteNumber,
        long? expectedRevision,
        string updateClause,
        Action<NpgsqlCommand> addParameters,
        string eventType,
        string description,
        CancellationToken cancellationToken,
        Func<NpgsqlConnection, NpgsqlTransaction, int, CancellationToken, Task>? mutate = null)
    {
        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            int id;
            long revision;
            string eventsJson;
            await using (var readCommand = connection.CreateCommand())
            {
                readCommand.Transaction = transaction;
                readCommand.CommandText = """
                    select "Id", "Revision", "EventsJson"
                    from "Quotes"
                    where "QuoteNumber" = @quoteNumber and not "IsDeleted"
                    for update;
                    """;
                readCommand.Parameters.AddWithValue("quoteNumber", quoteNumber);
                await using var reader = await readCommand.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException($"Preventivo {quoteNumber} non trovato.");
                id = reader.GetInt32(0);
                revision = reader.GetInt64(1);
                eventsJson = reader.GetString(2);
            }

            if (expectedRevision.HasValue && revision != expectedRevision.Value)
                throw new DatabaseWriteConflictException($"Il preventivo {quoteNumber} è stato modificato da un altro dispositivo. Riaprilo e riprova.");

            if (mutate != null) await mutate(connection, transaction, id, cancellationToken);

            string deviceName = GetDeviceName();
            DateTime savedAtUtc = DateTime.UtcNow;
            List<QuoteEventRecord> events = DeserializeJson<List<QuoteEventRecord>>(eventsJson) ?? [];
            events.Add(new QuoteEventRecord
            {
                CreatedAtUtc = savedAtUtc,
                DeviceName = deviceName,
                EventType = eventType,
                Description = description
            });

            await using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = $"""
                update "Quotes"
                set {updateClause},
                    "EventsJson" = @eventsJson,
                    "LastModifiedByDevice" = @device,
                    "LastModifiedUtc" = @savedAtUtc,
                    "Revision" = "Revision" + 1,
                    "SyncHash" = ''
                where "Id" = @id and "Revision" = @expectedRevision
                returning "Revision";
                """;
            updateCommand.Parameters.AddWithValue("eventsJson", JsonSerializer.Serialize(events));
            updateCommand.Parameters.AddWithValue("device", deviceName);
            updateCommand.Parameters.AddWithValue("savedAtUtc", savedAtUtc);
            updateCommand.Parameters.AddWithValue("id", id);
            updateCommand.Parameters.AddWithValue("expectedRevision", revision);
            addParameters(updateCommand);

            object? result = await updateCommand.ExecuteScalarAsync(cancellationToken);
            if (result is null or DBNull)
                throw new DatabaseWriteConflictException($"Il preventivo {quoteNumber} è cambiato durante il salvataggio.");
            await transaction.CommitAsync(cancellationToken);
            return Convert.ToInt64(result);
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch { /* Preserve the original database failure. */ }
            throw;
        }
    }

    private static void AddNullableDateTime(NpgsqlCommand command, string name, DateTime? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.TimestampTz);
        parameter.Value = value.HasValue ? EnsureUtc(value.Value.Date) : DBNull.Value;
    }
}
