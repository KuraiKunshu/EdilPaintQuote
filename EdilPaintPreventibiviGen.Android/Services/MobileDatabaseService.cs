using System.Data;
using EdilPaintPreventibiviGen.Android.Models;
using Npgsql;
using NpgsqlTypes;

namespace EdilPaintPreventibiviGen.Android.Services;

public sealed class MobileDatabaseService
{
    private const int CommandTimeoutSeconds = 20;

    public Task TestConnectionAsync(string connectionString) =>
        ExecuteReadWithRetryAsync(async cancellationToken =>
        {
            await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
            await using var command = new NpgsqlCommand("select 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        });

    public Task<IReadOnlyList<QuoteSummary>> GetQuoteSummariesAsync(
        string connectionString,
        string search,
        QuoteStatus? status,
        CancellationToken cancellationToken = default) =>
        ExecuteReadWithRetryAsync(
            token => GetQuoteSummariesOnceAsync(connectionString, search, status, token),
            cancellationToken);

    private static async Task<IReadOnlyList<QuoteSummary>> GetQuoteSummariesOnceAsync(
        string connectionString,
        string search,
        QuoteStatus? status,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        var where = new List<string> { "not q.\"IsDeleted\"" };
        await using var command = connection.CreateCommand();

        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(q.\"QuoteNumber\" ilike @search or c.\"BusinessName\" ilike @search or r.\"BusinessName\" ilike @search or b.\"BusinessName\" ilike @search or q.\"SiteName\" ilike @search or q.\"CustomerNotes\" ilike @search or q.\"Notes\" ilike @search)");
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
                q."Notes",
                q."CustomerNotes"
            from "Quotes" q
            left join "Customers" c on c."Id" = q."CustomerId"
            left join "Customers" r on r."Id" = q."ReferenceCustomerId"
            left join "Customers" b on b."Id" = q."BillingCustomerId"
            where {string.Join(" and ", where)}
            order by q."Date" desc, q."Id" desc
            limit 200;
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
                HasNotes = HasText(reader, 8) || HasText(reader, 9)
            });
        }

        return quotes;
    }

    public Task<QuoteDetail> GetQuoteAsync(
        string connectionString,
        string quoteNumber,
        CancellationToken cancellationToken = default) =>
        ExecuteReadWithRetryAsync(
            token => GetQuoteOnceAsync(connectionString, quoteNumber, token),
            cancellationToken);

    private static async Task<QuoteDetail> GetQuoteOnceAsync(
        string connectionString,
        string quoteNumber,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        QuoteDetail detail;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                select
                    q."Id",
                    q."QuoteNumber",
                    q."Date",
                    q."CustomerId",
                    c."SyncId",
                    coalesce(c."BusinessName", ''),
                    q."ReferenceCustomerId",
                    r."SyncId",
                    coalesce(r."BusinessName", ''),
                    q."BillingCustomerId",
                    b."SyncId",
                    coalesce(b."BusinessName", q."BillingCustomerName", ''),
                    q."SiteName",
                    q."PaymentTerms",
                    q."CustomerNotes",
                    q."IvaType",
                    q."Notes",
                    q."Imponibile",
                    q."Total",
                    q."MaterialDiscount",
                    q."LaborDiscount",
                    q."Status",
                    q."SentAtUtc",
                    q."SentRecipient",
                    q."LastModifiedByDevice",
                    q."LastModifiedUtc",
                    q."Revision"
                from "Quotes" q
                left join "Customers" c on c."Id" = q."CustomerId"
                left join "Customers" r on r."Id" = q."ReferenceCustomerId"
                left join "Customers" b on b."Id" = q."BillingCustomerId"
                where not q."IsDeleted" and q."QuoteNumber" = @quoteNumber
                limit 1;
                """;
            command.Parameters.AddWithValue("quoteNumber", quoteNumber);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException($"Preventivo {quoteNumber} non trovato.");

            detail = new QuoteDetail
            {
                Id = reader.GetInt32(0),
                QuoteNumber = reader.GetString(1),
                Date = reader.GetDateTime(2),
                CustomerId = GetNullableInt32(reader, 3),
                CustomerSyncId = GetNullableGuid(reader, 4),
                CustomerName = reader.GetString(5),
                ReferenceCustomerId = GetNullableInt32(reader, 6),
                ReferenceCustomerSyncId = GetNullableGuid(reader, 7),
                ReferenceName = reader.GetString(8),
                BillingCustomerId = GetNullableInt32(reader, 9),
                BillingCustomerSyncId = GetNullableGuid(reader, 10),
                BillingCustomerName = reader.GetString(11),
                SiteName = reader.GetString(12),
                PaymentTerms = reader.GetString(13),
                CustomerNotes = reader.GetString(14),
                IvaType = reader.GetString(15),
                Notes = reader.GetString(16),
                Imponibile = reader.GetDouble(17),
                Total = reader.GetDouble(18),
                MaterialDiscount = reader.GetDouble(19),
                LaborDiscount = reader.GetDouble(20),
                Status = (QuoteStatus)reader.GetInt32(21),
                SentAtUtc = reader.IsDBNull(22) ? null : reader.GetDateTime(22),
                SentRecipient = reader.GetString(23),
                LastModifiedByDevice = reader.GetString(24),
                LastModifiedUtc = reader.GetDateTime(25),
                Revision = reader.GetInt64(26)
            };
        }

        detail.Materials.AddRange(await LoadLinesAsync(connection, detail.Id, "QuoteMaterials", cancellationToken));
        detail.Labors.AddRange(await LoadLinesAsync(connection, detail.Id, "QuoteLabors", cancellationToken));
        return detail;
    }

    public Task<IReadOnlyList<CustomerRecord>> GetCustomersAsync(
        string connectionString,
        string search = "",
        CancellationToken cancellationToken = default) =>
        ExecuteReadWithRetryAsync(
            token => GetCustomersOnceAsync(connectionString, search, token),
            cancellationToken);

    private static async Task<IReadOnlyList<CustomerRecord>> GetCustomersOnceAsync(
        string connectionString,
        string search,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var command = connection.CreateCommand();
        string searchClause = string.Empty;
        if (!string.IsNullOrWhiteSpace(search))
        {
            searchClause = "and (\"BusinessName\" ilike @search or \"Address\" ilike @search or \"Email\" ilike @search or \"Phone\" ilike @search)";
            command.Parameters.AddWithValue("search", $"%{search.Trim()}%");
        }

        command.CommandText = $"""
            select "Id", "SyncId", "BusinessName", "Address", "Email", "Phone",
                   "MaterialDiscount", "LaborDiscount", "LastModifiedUtc"
            from "Customers"
            where not "IsDeleted" and not "IsSupplier" {searchClause}
            order by "BusinessName"
            limit 500;
            """;

        var customers = new List<CustomerRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            customers.Add(new CustomerRecord
            {
                Id = reader.GetInt32(0),
                SyncId = reader.GetGuid(1),
                BusinessName = reader.GetString(2),
                Address = reader.GetString(3),
                Email = reader.GetString(4),
                Phone = reader.GetString(5),
                MaterialDiscount = reader.GetDouble(6),
                LaborDiscount = reader.GetDouble(7),
                LastModifiedUtc = reader.GetDateTime(8)
            });
        }

        return customers;
    }

    public async Task<CustomerRecord> SaveCustomerAsync(
        string connectionString,
        CustomerRecord customer,
        CancellationToken cancellationToken = default)
    {
        NormalizeCustomer(customer);
        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var command = connection.CreateCommand();
        DateTime savedAtUtc = DateTime.UtcNow;

        AddCustomerParameters(command, customer, savedAtUtc);
        if (customer.Id == 0)
        {
            customer.SyncId = customer.SyncId == Guid.Empty ? Guid.NewGuid() : customer.SyncId;
            command.Parameters.AddWithValue("syncId", customer.SyncId);
            command.CommandText = """
                insert into "Customers"
                    ("SyncId", "BusinessName", "Address", "Email", "Phone",
                     "MaterialDiscount", "LaborDiscount", "SupplierDiscount", "IsSupplier",
                     "LastModifiedUtc", "IsDeleted")
                values
                    (@syncId, @businessName, @address, @email, @phone,
                     @materialDiscount, @laborDiscount, 0, false, @savedAtUtc, false)
                returning "Id", "LastModifiedUtc";
                """;
        }
        else
        {
            command.Parameters.AddWithValue("syncId", customer.SyncId);
            command.Parameters.AddWithValue("expectedLastModifiedUtc", EnsureUtc(customer.LastModifiedUtc));
            command.CommandText = """
                update "Customers"
                set "BusinessName" = @businessName,
                    "Address" = @address,
                    "Email" = @email,
                    "Phone" = @phone,
                    "MaterialDiscount" = @materialDiscount,
                    "LaborDiscount" = @laborDiscount,
                    "LastModifiedUtc" = @savedAtUtc
                where "SyncId" = @syncId
                  and "LastModifiedUtc" = @expectedLastModifiedUtc
                  and not "IsDeleted"
                  and not "IsSupplier"
                returning "Id", "LastModifiedUtc";
                """;
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new DatabaseWriteConflictException(
                "Questo cliente è stato modificato da un altro dispositivo. Riaprilo per caricare la versione più recente.");
        }

        customer.Id = reader.GetInt32(0);
        customer.LastModifiedUtc = reader.GetDateTime(1);
        return customer;
    }

    public Task<IReadOnlyList<CatalogItem>> GetCatalogAsync(
        string connectionString,
        QuoteLineKind kind,
        string search,
        CancellationToken cancellationToken = default) =>
        ExecuteReadWithRetryAsync(
            token => GetCatalogOnceAsync(connectionString, kind, search, token),
            cancellationToken);

    private static async Task<IReadOnlyList<CatalogItem>> GetCatalogOnceAsync(
        string connectionString,
        QuoteLineKind kind,
        string search,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var command = connection.CreateCommand();
        string searchClause = string.Empty;
        if (!string.IsNullOrWhiteSpace(search))
        {
            searchClause = "where (\"Name\" ilike @search or \"Description\" ilike @search)";
            command.Parameters.AddWithValue("search", $"%{search.Trim()}%");
        }

        command.CommandText = kind == QuoteLineKind.Material
            ? $"""
                select "Id", "Name", "Description", "UnitPrice", "IsSignificant"
                from "PersonalMaterials" {searchClause}
                order by "Name"
                limit 200;
                """
            : $"""
                select "Id", "Name", "Description", "UnitPrice", false
                from "LaborCatalog" {searchClause}
                order by "Name"
                limit 200;
                """;

        var items = new List<CatalogItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CatalogItem
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                UnitPrice = reader.GetDouble(3),
                IsSignificant = reader.GetBoolean(4)
            });
        }

        return items;
    }

    public Task<QuoteEditorDefaults> GetQuoteEditorDefaultsAsync(
        string connectionString,
        CancellationToken cancellationToken = default) =>
        ExecuteReadWithRetryAsync(async token =>
        {
            await using var connection = await OpenConnectionAsync(connectionString, token);
            await using var command = new NpgsqlCommand(
                "select coalesce(\"TerminiPagamento\", '') from \"CompanySettings\" order by \"Id\" limit 1;",
                connection);
            object? value = await command.ExecuteScalarAsync(token);
            return new QuoteEditorDefaults(value as string ?? string.Empty);
        }, cancellationToken);

    public async Task<QuoteSaveResult> SaveQuoteAsync(
        string connectionString,
        QuoteDraft draft,
        CancellationToken cancellationToken = default)
    {
        NormalizeAndValidateQuote(draft);
        QuoteTotals totals = QuoteTotalsCalculator.Calculate(
            draft.Materials,
            draft.Labors,
            draft.MaterialDiscount,
            draft.LaborDiscount,
            draft.IvaType);

        await using var connection = await OpenConnectionAsync(connectionString, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            int customerId = await ResolveCustomerIdAsync(
                connection,
                transaction,
                draft.CustomerId,
                draft.CustomerSyncId,
                required: true,
                cancellationToken) ?? throw new InvalidOperationException("Seleziona un cliente valido.");
            int? referenceCustomerId = await ResolveCustomerIdAsync(
                connection,
                transaction,
                draft.ReferenceCustomerId,
                draft.ReferenceCustomerSyncId,
                required: false,
                cancellationToken);
            int? billingCustomerId = await ResolveCustomerIdAsync(
                connection,
                transaction,
                draft.BillingCustomerId,
                draft.BillingCustomerSyncId,
                required: false,
                cancellationToken);

            string deviceName = GetDeviceName();
            DateTime savedAtUtc = DateTime.UtcNow;
            int quoteId;
            long revision;

            if (draft.IsNew)
            {
                draft.QuoteNumber = (await AllocateQuoteNumberAsync(connection, transaction, cancellationToken)).ToString();
                (quoteId, revision) = await InsertQuoteAsync(
                    connection,
                    transaction,
                    draft,
                    customerId,
                    referenceCustomerId,
                    billingCustomerId,
                    totals,
                    deviceName,
                    savedAtUtc,
                    cancellationToken);
            }
            else
            {
                (quoteId, revision) = await UpdateQuoteAsync(
                    connection,
                    transaction,
                    draft,
                    customerId,
                    referenceCustomerId,
                    billingCustomerId,
                    totals,
                    deviceName,
                    savedAtUtc,
                    cancellationToken);
            }

            await ReplaceLinesAsync(connection, transaction, quoteId, "QuoteMaterials", draft.Materials, cancellationToken);
            await ReplaceLinesAsync(connection, transaction, quoteId, "QuoteLabors", draft.Labors, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            draft.Id = quoteId;
            draft.Revision = revision;
            return new QuoteSaveResult(quoteId, draft.QuoteNumber, revision);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Preserve the original write failure if the connection was lost.
            }
            throw;
        }
    }

    public static string GetUserMessage(Exception exception)
    {
        Exception root = exception.GetBaseException();
        if (exception is DatabaseWriteConflictException || root is DatabaseWriteConflictException)
            return root.Message;

        if (root is PostgresException postgres)
        {
            return postgres.SqlState switch
            {
                PostgresErrorCodes.InsufficientPrivilege =>
                    "L'utente Neon salvato sul dispositivo non ha ancora i permessi di scrittura per preventivi e clienti.",
                PostgresErrorCodes.UniqueViolation =>
                    "Esiste già un elemento con gli stessi dati. Aggiorna l'elenco e riprova.",
                PostgresErrorCodes.ForeignKeyViolation =>
                    "Uno dei clienti selezionati non è più disponibile. Aggiorna l'elenco e riprova.",
                _ => postgres.MessageText
            };
        }

        return root.Message;
    }

    private static async Task<(int Id, long Revision)> InsertQuoteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        QuoteDraft draft,
        int customerId,
        int? referenceCustomerId,
        int? billingCustomerId,
        QuoteTotals totals,
        string deviceName,
        DateTime savedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into "Quotes"
                ("QuoteNumber", "Date", "CustomerId", "ReferenceCustomerId", "BillingCustomerId",
                 "SiteName", "BillingCustomerName", "PdfPath", "PaymentTerms", "CustomerNotes",
                 "IvaType", "Notes", "Imponibile", "MaterialDiscount", "LaborDiscount", "Total",
                 "Status", "CreatedByDevice", "LastModifiedByDevice", "SentMethod", "SentRecipient",
                 "SentByDevice", "ReminderCount", "LastReminderByDevice", "EventsJson", "SupplierName",
                 "MaterialStatus", "IsJointVenture", "PartnerCompanyName", "CostAllocationsJson",
                 "LastModifiedUtc", "Revision", "SyncHash", "IsDeleted")
            values
                (@quoteNumber, @date, @customerId, @referenceCustomerId, @billingCustomerId,
                 @siteName, @billingCustomerName, '', @paymentTerms, @customerNotes,
                 @ivaType, @notes, @imponibile, @materialDiscount, @laborDiscount, @total,
                 @status, @deviceName, @deviceName, '', '', '', 0, '', '[]', '', '', false, '', '',
                 @savedAtUtc, 1, '', false)
            returning "Id", "Revision";
            """;
        AddQuoteParameters(command, draft, customerId, referenceCustomerId, billingCustomerId, totals, deviceName, savedAtUtc);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Il preventivo non è stato creato.");
        return (reader.GetInt32(0), reader.GetInt64(1));
    }

    private static async Task<(int Id, long Revision)> UpdateQuoteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        QuoteDraft draft,
        int customerId,
        int? referenceCustomerId,
        int? billingCustomerId,
        QuoteTotals totals,
        string deviceName,
        DateTime savedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            update "Quotes"
            set "Date" = @date,
                "CustomerId" = @customerId,
                "ReferenceCustomerId" = @referenceCustomerId,
                "BillingCustomerId" = @billingCustomerId,
                "SiteName" = @siteName,
                "BillingCustomerName" = @billingCustomerName,
                "PaymentTerms" = @paymentTerms,
                "CustomerNotes" = @customerNotes,
                "IvaType" = @ivaType,
                "Notes" = @notes,
                "Imponibile" = @imponibile,
                "MaterialDiscount" = @materialDiscount,
                "LaborDiscount" = @laborDiscount,
                "Total" = @total,
                "Status" = @status,
                "LastModifiedByDevice" = @deviceName,
                "LastModifiedUtc" = @savedAtUtc,
                "Revision" = "Revision" + 1,
                "SyncHash" = ''
            where "Id" = @id
              and "QuoteNumber" = @quoteNumber
              and "Revision" = @expectedRevision
              and not "IsDeleted"
            returning "Id", "Revision";
            """;
        AddQuoteParameters(command, draft, customerId, referenceCustomerId, billingCustomerId, totals, deviceName, savedAtUtc);
        command.Parameters.AddWithValue("id", draft.Id);
        command.Parameters.AddWithValue("expectedRevision", draft.Revision);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new DatabaseWriteConflictException(
                $"Il preventivo {draft.QuoteNumber} è stato modificato da un altro dispositivo. Riaprilo per caricare la versione più recente.");
        }

        return (reader.GetInt32(0), reader.GetInt64(1));
    }

    private static void AddQuoteParameters(
        NpgsqlCommand command,
        QuoteDraft draft,
        int customerId,
        int? referenceCustomerId,
        int? billingCustomerId,
        QuoteTotals totals,
        string deviceName,
        DateTime savedAtUtc)
    {
        command.Parameters.AddWithValue("quoteNumber", draft.QuoteNumber);
        command.Parameters.AddWithValue("date", EnsureUtc(draft.Date.Date));
        command.Parameters.AddWithValue("customerId", customerId);
        AddNullableInt(command, "referenceCustomerId", referenceCustomerId);
        AddNullableInt(command, "billingCustomerId", billingCustomerId);
        command.Parameters.AddWithValue("siteName", draft.SiteName);
        command.Parameters.AddWithValue("billingCustomerName", draft.BillingCustomerName);
        command.Parameters.AddWithValue("paymentTerms", draft.PaymentTerms);
        command.Parameters.AddWithValue("customerNotes", draft.CustomerNotes);
        command.Parameters.AddWithValue("ivaType", QuoteTotalsCalculator.NormalizeIvaType(draft.IvaType));
        command.Parameters.AddWithValue("notes", draft.Notes);
        command.Parameters.AddWithValue("imponibile", totals.Imponibile);
        command.Parameters.AddWithValue("materialDiscount", draft.MaterialDiscount);
        command.Parameters.AddWithValue("laborDiscount", draft.LaborDiscount);
        command.Parameters.AddWithValue("total", totals.Total);
        command.Parameters.AddWithValue("status", (int)draft.Status);
        command.Parameters.AddWithValue("deviceName", deviceName);
        command.Parameters.AddWithValue("savedAtUtc", savedAtUtc);
    }

    private static async Task ReplaceLinesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int quoteId,
        string tableName,
        IEnumerable<QuoteLine> lines,
        CancellationToken cancellationToken)
    {
        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = $"delete from \"{tableName}\" where \"QuoteId\" = @quoteId;";
            deleteCommand.Parameters.AddWithValue("quoteId", quoteId);
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        int sortOrder = 0;
        foreach (var line in lines)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = $"""
                insert into "{tableName}"
                    ("QuoteId", "CatalogItemId", "Name", "Description", "UnitPrice",
                     "Quantity", "Discount", "IsSignificant", "SortOrder")
                values
                    (@quoteId, @catalogItemId, @name, @description, @unitPrice,
                     @quantity, @discount, @isSignificant, @sortOrder);
                """;
            insertCommand.Parameters.AddWithValue("quoteId", quoteId);
            insertCommand.Parameters.AddWithValue("catalogItemId", Math.Max(0, line.CatalogItemId));
            insertCommand.Parameters.AddWithValue("name", line.Name);
            insertCommand.Parameters.AddWithValue("description", line.Description);
            insertCommand.Parameters.AddWithValue("unitPrice", line.UnitPrice);
            insertCommand.Parameters.AddWithValue("quantity", line.Quantity);
            insertCommand.Parameters.AddWithValue("discount", line.Discount);
            insertCommand.Parameters.AddWithValue("isSignificant", line.IsSignificant);
            insertCommand.Parameters.AddWithValue("sortOrder", sortOrder++);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<int> AllocateQuoteNumberAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            with first_settings as (
                select "Id"
                from "CompanySettings"
                order by "Id"
                limit 1
            ),
            max_quote as (
                select coalesce(max(
                    case
                        when "QuoteNumber" ~ '^[0-9]+'
                        then substring("QuoteNumber" from '^[0-9]+')::integer
                        else null
                    end
                ), 0) as "MaxQuoteNumber"
                from "Quotes"
                where not "IsDeleted"
            )
            update "CompanySettings" as settings
            set "Counter" = greatest(settings."Counter", max_quote."MaxQuoteNumber") + 1
            from first_settings, max_quote
            where settings."Id" = first_settings."Id"
            returning settings."Counter";
            """;
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
            throw new InvalidOperationException("Configurazione azienda non trovata: impossibile assegnare il numero del preventivo.");
        return Convert.ToInt32(result);
    }

    private static async Task<int?> ResolveCustomerIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int? id,
        Guid syncId,
        bool required,
        CancellationToken cancellationToken)
    {
        if (!id.HasValue && syncId == Guid.Empty)
            return required ? throw new InvalidOperationException("Seleziona un cliente.") : null;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (syncId != Guid.Empty)
        {
            command.CommandText = """
                select "Id" from "Customers"
                where "SyncId" = @syncId and not "IsDeleted" and not "IsSupplier"
                limit 1;
                """;
            command.Parameters.AddWithValue("syncId", syncId);
        }
        else
        {
            command.CommandText = """
                select "Id" from "Customers"
                where "Id" = @id and not "IsDeleted" and not "IsSupplier"
                limit 1;
                """;
            command.Parameters.AddWithValue("id", id!.Value);
        }

        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
        {
            if (required)
                throw new InvalidOperationException("Il cliente selezionato non è più disponibile.");
            return null;
        }

        return Convert.ToInt32(result);
    }

    private static async Task<List<QuoteLine>> LoadLinesAsync(
        NpgsqlConnection connection,
        int quoteId,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            select "CatalogItemId", "Name", "Description", "UnitPrice", "Quantity",
                   "Discount", "IsSignificant", "SortOrder"
            from "{tableName}"
            where "QuoteId" = @quoteId
            order by "SortOrder", "Id";
            """;
        command.Parameters.AddWithValue("quoteId", quoteId);

        var lines = new List<QuoteLine>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new QuoteLine
            {
                CatalogItemId = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                UnitPrice = reader.GetDouble(3),
                Quantity = reader.GetInt32(4),
                Discount = reader.GetDouble(5),
                IsSignificant = reader.GetBoolean(6),
                SortOrder = reader.GetInt32(7)
            });
        }

        return lines;
    }

    private static void NormalizeCustomer(CustomerRecord customer)
    {
        customer.BusinessName = (customer.BusinessName ?? string.Empty).Trim();
        customer.Address = (customer.Address ?? string.Empty).Trim();
        customer.Email = (customer.Email ?? string.Empty).Trim();
        customer.Phone = (customer.Phone ?? string.Empty).Trim();
        customer.MaterialDiscount = Math.Clamp(customer.MaterialDiscount, 0, 100);
        customer.LaborDiscount = Math.Clamp(customer.LaborDiscount, 0, 100);

        if (string.IsNullOrWhiteSpace(customer.BusinessName))
            throw new InvalidOperationException("Inserisci la ragione sociale del cliente.");
        if (customer.BusinessName.Length > 250)
            throw new InvalidOperationException("La ragione sociale può contenere al massimo 250 caratteri.");
    }

    private static void NormalizeAndValidateQuote(QuoteDraft draft)
    {
        draft.SiteName = (draft.SiteName ?? string.Empty).Trim();
        draft.BillingCustomerName = (draft.BillingCustomerName ?? string.Empty).Trim();
        draft.PaymentTerms = (draft.PaymentTerms ?? string.Empty).Trim();
        draft.CustomerNotes = (draft.CustomerNotes ?? string.Empty).Trim();
        draft.Notes = (draft.Notes ?? string.Empty).Trim();
        draft.IvaType = QuoteTotalsCalculator.NormalizeIvaType(draft.IvaType);
        draft.MaterialDiscount = Math.Clamp(draft.MaterialDiscount, 0, 100);
        draft.LaborDiscount = Math.Clamp(draft.LaborDiscount, 0, 100);

        if (draft.Materials.Count == 0 && draft.Labors.Count == 0)
            throw new InvalidOperationException("Inserisci almeno un materiale o una lavorazione.");

        foreach (var line in draft.Materials.Concat(draft.Labors))
        {
            line.Name = (line.Name ?? string.Empty).Trim();
            line.Description = (line.Description ?? string.Empty).Trim();
            line.Discount = Math.Clamp(line.Discount, 0, 100);
            if (string.IsNullOrWhiteSpace(line.Name))
                throw new InvalidOperationException("Ogni riga deve avere un nome.");
            if (line.Quantity <= 0)
                throw new InvalidOperationException($"La quantità di '{line.Name}' deve essere maggiore di zero.");
            if (line.UnitPrice < 0)
                throw new InvalidOperationException($"Il prezzo di '{line.Name}' non può essere negativo.");
        }
    }

    private static void AddCustomerParameters(NpgsqlCommand command, CustomerRecord customer, DateTime savedAtUtc)
    {
        command.Parameters.AddWithValue("businessName", customer.BusinessName);
        command.Parameters.AddWithValue("address", customer.Address);
        command.Parameters.AddWithValue("email", customer.Email);
        command.Parameters.AddWithValue("phone", customer.Phone);
        command.Parameters.AddWithValue("materialDiscount", customer.MaterialDiscount);
        command.Parameters.AddWithValue("laborDiscount", customer.LaborDiscount);
        command.Parameters.AddWithValue("savedAtUtc", savedAtUtc);
    }

    private static async Task<NpgsqlConnection> OpenConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(NormalizeConnectionString(connectionString));
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static string NormalizeConnectionString(string connectionString)
    {
        connectionString = connectionString.Trim();
        if (connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertUriConnectionString(connectionString);
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            ApplicationName = "EdilPaint Mobile"
        };
        if (builder.SslMode == SslMode.Prefer)
            builder.SslMode = SslMode.Require;
        if (builder.Timeout <= 0)
            builder.Timeout = 15;
        if (builder.CommandTimeout <= 0)
            builder.CommandTimeout = CommandTimeoutSeconds;
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
            CommandTimeout = CommandTimeoutSeconds,
            ApplicationName = "EdilPaint Mobile"
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
                TrySetConnectionStringValue(builder, "Channel Binding", NormalizeRequirePreferDisable(value));
        }

        return builder.ConnectionString;
    }

    private static async Task<T> ExecuteReadWithRetryAsync<T>(
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
            catch (PostgresException)
            {
                throw;
            }
            catch (Exception exception) when (attempt < maxAttempts && IsRetryable(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), cancellationToken);
            }
        }
    }

    private static bool IsRetryable(Exception exception)
    {
        if (exception is NpgsqlException)
            return true;

        string message = exception.GetBaseException().Message;
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
            // Optional Neon parameters differ between provider versions.
        }
    }

    private static void AddNullableInt(NpgsqlCommand command, string name, int? value)
    {
        NpgsqlParameter parameter = command.Parameters.Add(name, NpgsqlDbType.Integer);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static int? GetNullableInt32(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static Guid GetNullableGuid(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? Guid.Empty : reader.GetGuid(ordinal);

    private static bool HasText(NpgsqlDataReader reader, int ordinal) =>
        !reader.IsDBNull(ordinal) && !string.IsNullOrWhiteSpace(reader.GetString(ordinal));

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static string GetDeviceName()
    {
        string name = DeviceInfo.Current.Name;
        string value = string.IsNullOrWhiteSpace(name) ? "Android" : $"Android - {name.Trim()}";
        return value.Length <= 120 ? value : value[..120];
    }
}
