using System.Net.Mail;
using System.Text.RegularExpressions;

namespace EdilPaintPreventibiviGen.Services;

public sealed record EmailRecipientSplit(string PrimaryRecipient, List<string> CopyRecipients);

public static partial class EmailAddressParser
{
    public static EmailRecipientSplit SplitPrimaryAndCopies(string? value)
    {
        var emails = ExtractEmails(value);
        if (emails.Count == 0)
            return new EmailRecipientSplit(string.Empty, []);

        return new EmailRecipientSplit(emails[0], emails.Skip(1).ToList());
    }

    public static List<string> ExtractEmails(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in EmailRegex().Matches(value))
        {
            string address = match.Value.Trim();
            if (!TryNormalize(address, out string normalized))
                continue;

            if (seen.Add(normalized))
                result.Add(normalized);
        }

        return result;
    }

    public static string Join(IEnumerable<string> emails) =>
        string.Join("; ", emails.Where(x => !string.IsNullOrWhiteSpace(x)));

    public static List<MailAddress> ToMailAddresses(string? value) =>
        ExtractEmails(value).Select(x => new MailAddress(x)).ToList();

    private static bool TryNormalize(string value, out string normalized)
    {
        normalized = string.Empty;

        try
        {
            var address = new MailAddress(value);
            if (!string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase))
                return false;

            normalized = address.Address;
            return true;
        }
        catch
        {
            return false;
        }
    }

    [GeneratedRegex(@"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
