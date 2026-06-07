using System.IO;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed class PdfArchiveAuditService
{
    private const int DefaultScanLimit = 300;

    private readonly IDataService _dataService;
    private readonly QuoteHistoryService _historyService;

    public PdfArchiveAuditService(IDataService dataService, StoragePathService storagePathService)
    {
        _dataService = dataService;
        _historyService = new QuoteHistoryService(dataService, storagePathService);
    }

    public async Task<List<PdfArchiveIssue>> ScanAsync(
        int take = DefaultScanLimit,
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

            await CheckOfficialPdfAsync(entry, issues, cancellationToken);
            await CheckCostsPdfAsync(entry, issues, cancellationToken);
            await CheckAttachmentsAsync(summary.QuoteNumber, issues, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        string expectedPath = _historyService.GetExpectedPdfPath(entry);
        if (File.Exists(expectedPath))
            return;

        await Task.CompletedTask;
        issues.Add(CreateIssue(entry, PdfArchiveIssueType.OfficialPdfMissing, expectedPath, false));
    }

    private async Task CheckCostsPdfAsync(
        QuoteHistoryEntry entry,
        List<PdfArchiveIssue> issues,
        CancellationToken cancellationToken)
    {
        if (!entry.IsJointVenture)
            return;

        string expectedPath = _historyService.GetExpectedCostsPdfPath(entry);
        if (File.Exists(expectedPath))
            return;

        await Task.CompletedTask;
        issues.Add(CreateIssue(entry, PdfArchiveIssueType.CostsPdfMissing, expectedPath, false));
    }

    private async Task CheckAttachmentsAsync(
        string quoteNumber,
        List<PdfArchiveIssue> issues,
        CancellationToken cancellationToken)
    {
        var entry = await _historyService.GetQuoteByNumberAsync(quoteNumber).ConfigureAwait(false);
        if (entry == null)
            return;

        cancellationToken.ThrowIfCancellationRequested();

        List<StoredFile> attachments = entry.Attachments;

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
                : "Non ci sono dati incorporati per ripristinarlo: serve rigenerarlo dal preventivo."
        };
    }
}
