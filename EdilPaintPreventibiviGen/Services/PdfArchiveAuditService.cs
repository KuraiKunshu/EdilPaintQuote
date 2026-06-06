using System.IO;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed class PdfArchiveAuditService
{
    private readonly IDataService _dataService;
    private readonly QuoteHistoryService _historyService;

    public PdfArchiveAuditService(IDataService dataService, StoragePathService storagePathService)
    {
        _dataService = dataService;
        _historyService = new QuoteHistoryService(dataService, storagePathService);
    }

    public async Task<List<PdfArchiveIssue>> ScanAsync(
        int take = int.MaxValue,
        bool includeDatabaseChecks = true,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<PdfArchiveIssue>();
        var summaries = await _dataService
            .GetQuoteSummariesAsync(take, cancellationToken)
            .ConfigureAwait(false);

        foreach (var summary in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = new QuoteHistoryEntry
            {
                QuoteNumber = summary.QuoteNumber,
                Date = summary.Date.DateTime,
                CustomerName = summary.CustomerName,
                ReferenceName = summary.ReferenceName,
                PdfPath = summary.PdfPath,
                IsJointVenture = summary.IsJointVenture,
                PartnerCompanyName = summary.PartnerCompanyName
            };

            await CheckOfficialPdfAsync(entry, issues, includeDatabaseChecks, cancellationToken);
            await CheckCostsPdfAsync(entry, issues, includeDatabaseChecks, cancellationToken);

            if (includeDatabaseChecks)
                await CheckAttachmentsAsync(entry, issues, cancellationToken);
        }

        return issues;
    }

    public async Task RestoreAsync(PdfArchiveIssue issue, CancellationToken cancellationToken = default)
    {
        var entry = await _historyService
            .GetQuoteByNumberAsync(issue.QuoteNumber)
            .ConfigureAwait(false);
        if (entry == null)
            return;

        switch (issue.Type)
        {
            case PdfArchiveIssueType.OfficialPdfMissing:
                await _historyService.EnsureOfficialPdfExistsAsync(entry, cancellationToken).ConfigureAwait(false);
                break;
            case PdfArchiveIssueType.CostsPdfMissing:
                await _historyService.EnsureCostsPdfExistsAsync(entry, cancellationToken).ConfigureAwait(false);
                break;
            case PdfArchiveIssueType.AttachmentsMissing:
                await _historyService.EnsureAttachmentsFolderExistsAsync(entry).ConfigureAwait(false);
                break;
        }
    }

    private async Task CheckOfficialPdfAsync(
        QuoteHistoryEntry entry,
        List<PdfArchiveIssue> issues,
        bool includeDatabaseChecks,
        CancellationToken cancellationToken)
    {
        string expectedPath = _historyService.GetExpectedPdfPath(entry);
        if (File.Exists(expectedPath))
            return;

        if (!includeDatabaseChecks)
        {
            issues.Add(CreateIssue(entry, PdfArchiveIssueType.OfficialPdfMissing, expectedPath, true));
            return;
        }

        byte[]? dbPdf = null;
        try { dbPdf = await _dataService.GetQuotePdfContentAsync(entry.QuoteNumber, cancellationToken).ConfigureAwait(false); }
        catch { }

        issues.Add(dbPdf is { Length: > 0 }
            ? CreateIssue(entry, PdfArchiveIssueType.OfficialPdfMissing, expectedPath, true)
            : CreateIssue(entry, PdfArchiveIssueType.OfficialPdfMissingInDatabase, expectedPath, false));
    }

    private async Task CheckCostsPdfAsync(
        QuoteHistoryEntry entry,
        List<PdfArchiveIssue> issues,
        bool includeDatabaseChecks,
        CancellationToken cancellationToken)
    {
        if (!entry.IsJointVenture)
            return;

        string expectedPath = _historyService.GetExpectedCostsPdfPath(entry);
        if (File.Exists(expectedPath))
            return;

        if (!includeDatabaseChecks)
        {
            issues.Add(CreateIssue(entry, PdfArchiveIssueType.CostsPdfMissing, expectedPath, true));
            return;
        }

        byte[]? dbPdf = null;
        try { dbPdf = await _dataService.GetQuoteCostsPdfContentAsync(entry.QuoteNumber, cancellationToken).ConfigureAwait(false); }
        catch { }

        issues.Add(dbPdf is { Length: > 0 }
            ? CreateIssue(entry, PdfArchiveIssueType.CostsPdfMissing, expectedPath, true)
            : CreateIssue(entry, PdfArchiveIssueType.CostsPdfMissingInDatabase, expectedPath, false));
    }

    private async Task CheckAttachmentsAsync(
        QuoteHistoryEntry entry,
        List<PdfArchiveIssue> issues,
        CancellationToken cancellationToken)
    {
        List<StoredFile> attachments;
        try { attachments = await _dataService.GetQuoteAttachmentsAsync(entry.QuoteNumber).ConfigureAwait(false); }
        catch { return; }

        if (attachments.Count == 0)
            return;

        string? parentDir = Path.GetDirectoryName(_historyService.GetExpectedPdfPath(entry));
        if (string.IsNullOrWhiteSpace(parentDir))
            return;

        string attachmentsDir = Path.Combine(parentDir, "Allegati_" + entry.QuoteNumber);
        bool hasMissing = !Directory.Exists(attachmentsDir) ||
                          attachments.Any(a => !File.Exists(Path.Combine(attachmentsDir, Path.GetFileName(a.FileName))));

        if (hasMissing)
        {
            issues.Add(CreateIssue(
                entry,
                PdfArchiveIssueType.AttachmentsMissing,
                attachmentsDir,
                attachments.Any(a => a.Content.Length > 0)));
        }
    }

    private static PdfArchiveIssue CreateIssue(
        QuoteHistoryEntry entry,
        PdfArchiveIssueType type,
        string expectedPath,
        bool canRestore)
    {
        return new PdfArchiveIssue
        {
            QuoteNumber = entry.QuoteNumber,
            CustomerName = entry.CustomerName,
            QuoteDate = entry.Date,
            Type = type,
            ExpectedPath = expectedPath,
            CanRestore = canRestore,
            Details = canRestore
                ? "Il file manca dalla cartella, ma ci sono dati per ripristinarlo."
                : "Il database non contiene il file: serve rigenerarlo dal preventivo."
        };
    }
}
