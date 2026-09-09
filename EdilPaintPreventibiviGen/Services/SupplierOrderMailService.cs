using System.Diagnostics;
using System.Globalization;
using System.Text;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed class SupplierOrderMailDraft
{
    public string Recipient { get; set; } = string.Empty;
    public string CcRecipients { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string MailToUri { get; set; } = string.Empty;
}

public static class SupplierOrderMailService
{
    public static SupplierOrderMailDraft CreateDraft(
        string recipient,
        string subject,
        string body,
        string ccRecipients = "")
    {
        recipient = recipient.Trim();
        ccRecipients = ccRecipients.Trim();
        subject = subject.Trim();

        return new SupplierOrderMailDraft
        {
            Recipient = recipient,
            CcRecipients = ccRecipients,
            Subject = subject,
            Body = body,
            MailToUri = BuildMailToUri(recipient, ccRecipients, subject, body)
        };
    }

    public static SupplierOrderMailDraft CreateDraft(
        QuoteHistoryEntry quote,
        IEnumerable<Customer> customers)
        => CreateDraft(quote, customers, App.AppSettings.Mail);

    public static SupplierOrderMailDraft CreateDraft(
        QuoteHistoryEntry quote,
        IEnumerable<Customer> customers,
        MailSettingsModel mailSettings)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(mailSettings);

        var supplier = ResolveSupplier(quote, customers);
        string recipient = supplier?.Email?.Trim() ?? string.Empty;
        string subjectTemplate = string.IsNullOrWhiteSpace(mailSettings.SupplierOrderSubjectTemplate)
            ? MailSettingsModel.DefaultSupplierOrderSubjectTemplate
            : mailSettings.SupplierOrderSubjectTemplate;
        string bodyTemplate = string.IsNullOrWhiteSpace(mailSettings.SupplierOrderBodyTemplate)
            ? MailSettingsModel.DefaultSupplierOrderBodyTemplate
            : mailSettings.SupplierOrderBodyTemplate;
        string materials = BuildMaterialsList(quote);
        string subject = FormatTemplate(subjectTemplate, quote, supplier, materials);
        string body = FormatTemplate(bodyTemplate, quote, supplier, materials);
        string ccRecipients = EnsureSenderCopy(recipient, string.Empty, mailSettings);

        return CreateDraft(recipient, subject, body, ccRecipients);
    }

    public static string EnsureSenderCopy(
        string recipient,
        string ccRecipients,
        MailSettingsModel mailSettings)
    {
        ArgumentNullException.ThrowIfNull(mailSettings);

        var recipientAddresses = EmailAddressParser.ExtractEmails(recipient)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var copies = EmailAddressParser.ExtractEmails(ccRecipients)
            .Where(address => !recipientAddresses.Contains(address))
            .ToList();
        string? senderAddress = EmailAddressParser
            .ExtractEmails(mailSettings.EffectiveSenderEmail)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(senderAddress) &&
            !recipientAddresses.Contains(senderAddress))
        {
            copies.Add(senderAddress);
        }

        return EmailAddressParser.Join(
            copies.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public static void OpenDraft(SupplierOrderMailDraft draft)
    {
        Process.Start(new ProcessStartInfo(draft.MailToUri) { UseShellExecute = true });
    }

    private static Customer? ResolveSupplier(QuoteHistoryEntry quote, IEnumerable<Customer> customers)
    {
        if (string.IsNullOrWhiteSpace(quote.SupplierName))
            return null;

        string supplierName = quote.SupplierName.Trim();
        return customers.FirstOrDefault(customer =>
            string.Equals(customer.BusinessName?.Trim(), supplierName, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveOrderReference(QuoteHistoryEntry quote)
    {
        if (!string.IsNullOrWhiteSpace(quote.ReferenceName))
            return quote.ReferenceName.Trim();

        if (!string.IsNullOrWhiteSpace(quote.CustomerName))
            return quote.CustomerName.Trim();

        return quote.QuoteNumber.Trim();
    }

    private static string FormatTemplate(
        string template,
        QuoteHistoryEntry quote,
        Customer? supplier,
        string materials)
    {
        return template
            .Replace("{OrderReference}", ResolveOrderReference(quote), StringComparison.OrdinalIgnoreCase)
            .Replace("{QuoteNumber}", quote.QuoteNumber ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{CustomerName}", quote.CustomerName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{ReferenceName}", quote.ReferenceName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{SupplierName}",
                supplier?.BusinessName?.Trim() ?? quote.SupplierName?.Trim() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "{Date}",
                quote.Date.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("it-IT")),
                StringComparison.OrdinalIgnoreCase)
            .Replace("{Materials}", materials, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildMaterialsList(QuoteHistoryEntry quote)
    {
        var body = new StringBuilder();
        var materials = quote.Materials
            .Where(material => !string.IsNullOrWhiteSpace(material.Name) && material.Quantity > 0)
            .OrderBy(material => material.SortOrder)
            .ThenBy(material => material.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (materials.Count == 0)
        {
            body.AppendLine("- Nessun materiale presente nel preventivo.");
        }
        else
        {
            foreach (var material in materials)
                body.AppendLine($"N.{material.Quantity} {material.Name.Trim()}");
        }

        return body.ToString().TrimEnd();
    }

    private static string BuildMailToUri(string recipient, string ccRecipients, string subject, string body)
    {
        string uri = string.IsNullOrWhiteSpace(recipient)
            ? "mailto:"
            : $"mailto:{Uri.EscapeDataString(recipient)}";

        var query = new List<string>
        {
            $"subject={Uri.EscapeDataString(subject)}",
            $"body={Uri.EscapeDataString(body)}"
        };

        if (!string.IsNullOrWhiteSpace(ccRecipients))
            query.Insert(0, $"cc={Uri.EscapeDataString(ccRecipients)}");

        return uri + "?" + string.Join("&", query);
    }
}
