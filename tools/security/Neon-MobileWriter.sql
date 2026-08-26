-- Eseguire nel SQL Editor di Neon come neondb_owner.
-- Lo script trasforma edilpaint_mobile in un utente operativo limitato a
-- preventivi, clienti e cataloghi. Non concede la cancellazione di preventivi
-- o clienti e non concede alcun accesso alla gestione fornitori.

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'edilpaint_mobile') THEN
        RAISE EXCEPTION 'Il ruolo edilpaint_mobile non esiste';
    END IF;
END
$$;

ALTER ROLE edilpaint_mobile RESET default_transaction_read_only;

GRANT CONNECT ON DATABASE neondb TO edilpaint_mobile;
GRANT USAGE ON SCHEMA public TO edilpaint_mobile;
REVOKE CREATE ON SCHEMA public FROM edilpaint_mobile;

REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA public FROM edilpaint_mobile;
REVOKE ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public FROM edilpaint_mobile;

GRANT SELECT ON TABLE
    "Customers",
    "Quotes",
    "QuoteMaterials",
    "QuoteLabors",
    "PersonalMaterials",
    "LaborCatalog",
    "CompanySettings"
TO edilpaint_mobile;

GRANT INSERT (
    "SyncId", "BusinessName", "Address", "Email", "Phone",
    "MaterialDiscount", "LaborDiscount", "SupplierDiscount", "IsSupplier",
    "LastModifiedUtc", "IsDeleted"
) ON TABLE "Customers" TO edilpaint_mobile;

GRANT UPDATE (
    "BusinessName", "Address", "Email", "Phone",
    "MaterialDiscount", "LaborDiscount", "LastModifiedUtc"
) ON TABLE "Customers" TO edilpaint_mobile;

GRANT INSERT (
    "QuoteNumber", "Date", "CustomerId", "ReferenceCustomerId", "BillingCustomerId",
    "SiteName", "BillingCustomerName", "PdfPath", "PaymentTerms", "CustomerNotes",
    "IvaType", "Notes", "Imponibile", "MaterialDiscount", "LaborDiscount", "Total",
    "Status", "CreatedByDevice", "LastModifiedByDevice", "SentMethod", "SentRecipient",
    "SentByDevice", "ReminderCount", "LastReminderByDevice", "EventsJson", "SupplierName",
    "MaterialStatus", "IsJointVenture", "PartnerCompanyName", "CostAllocationsJson",
    "LastModifiedUtc", "Revision", "SyncHash", "IsDeleted"
) ON TABLE "Quotes" TO edilpaint_mobile;

GRANT UPDATE (
    "Date", "CustomerId", "ReferenceCustomerId", "BillingCustomerId",
    "SiteName", "BillingCustomerName", "PaymentTerms", "CustomerNotes", "IvaType",
    "Notes", "Imponibile", "MaterialDiscount", "LaborDiscount", "Total", "Status",
    "LastModifiedByDevice", "LastModifiedUtc", "Revision", "SyncHash"
) ON TABLE "Quotes" TO edilpaint_mobile;

GRANT INSERT (
    "QuoteId", "CatalogItemId", "Name", "Description", "UnitPrice",
    "Quantity", "Discount", "IsSignificant", "SortOrder"
) ON TABLE "QuoteMaterials", "QuoteLabors" TO edilpaint_mobile;

GRANT DELETE ON TABLE "QuoteMaterials", "QuoteLabors" TO edilpaint_mobile;
GRANT UPDATE ("Counter") ON TABLE "CompanySettings" TO edilpaint_mobile;

DO $$
DECLARE
    table_name text;
    sequence_name text;
BEGIN
    FOREACH table_name IN ARRAY ARRAY['Customers', 'Quotes', 'QuoteMaterials', 'QuoteLabors']
    LOOP
        sequence_name := pg_get_serial_sequence(format('public.%I', table_name), 'Id');
        IF sequence_name IS NOT NULL THEN
            EXECUTE format(
                'GRANT USAGE, SELECT ON SEQUENCE %s TO %I',
                sequence_name,
                'edilpaint_mobile');
        END IF;
    END LOOP;
END
$$;

-- Controllo finale: ogni colonna deve restituire true.
SELECT
    has_table_privilege('edilpaint_mobile', 'public."Quotes"', 'SELECT') AS quotes_read,
    has_column_privilege('edilpaint_mobile', 'public."Quotes"', 'Total', 'UPDATE') AS quotes_write,
    has_table_privilege('edilpaint_mobile', 'public."Customers"', 'SELECT') AS customers_read,
    has_column_privilege('edilpaint_mobile', 'public."Customers"', 'BusinessName', 'UPDATE') AS customers_write,
    has_table_privilege('edilpaint_mobile', 'public."QuoteMaterials"', 'DELETE') AS lines_replace;
