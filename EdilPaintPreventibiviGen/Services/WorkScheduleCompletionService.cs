using System.Diagnostics;
using System.IO;
using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public sealed record WorkScheduleCompletionResult(WorkScheduleEntry Entry, string PdfPath);

public sealed class WorkScheduleCostsCalculationRequiredException : InvalidOperationException
{
    public string QuoteNumber { get; }

    public WorkScheduleCostsCalculationRequiredException(string quoteNumber)
        : base($"Salva prima il calcolo dei costi e del guadagno del preventivo {quoteNumber}.") =>
        QuoteNumber = quoteNumber;
}

public sealed class WorkScheduleCostsPublicationException : IOException
{
    public WorkScheduleEntry Entry { get; }
    public string PdfPath { get; }

    public WorkScheduleCostsPublicationException(WorkScheduleEntry entry, string pdfPath, Exception innerException)
        : base("L'intervento è salvato come finito, ma il PDF dei costi non è stato archiviato. " +
            "Puoi rigenerarlo dall'intervento senza modificare il calendario.", innerException)
    {
        Entry = entry.CreateValidatedCopy();
        PdfPath = pdfPath;
    }
}

/// <summary>Archives saved cost calculations and records completion without altering the saved visit.</summary>
public sealed class WorkScheduleCompletionService
{
    private readonly WorkScheduleService _schedule;
    private readonly IDataService _data;
    private readonly StoragePathService _storage;
    private readonly Action<RealProfitPdfContext, string> _writePdf;
    private readonly Func<DateTime> _clock;

    public WorkScheduleCompletionService(WorkScheduleService schedule, IDataService data, StoragePathService storage)
        : this(schedule, data, storage, (context, path) => new PdfService().GenerateRealProfitPdf(context, path))
    {
    }

    internal WorkScheduleCompletionService(WorkScheduleService schedule, IDataService data,
        StoragePathService storage, Action<RealProfitPdfContext, string> writePdf, Func<DateTime>? clock = null)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _writePdf = writePdf ?? throw new ArgumentNullException(nameof(writePdf));
        _clock = clock ?? (() => DateTime.Now);
    }

    public Task<WorkScheduleCompletionResult> CompleteAsync(WorkScheduleEntry selected,
        CancellationToken token = default) => GenerateAndCompleteAsync(selected, requireCompleted: false, token);

    public Task<WorkScheduleCompletionResult> GenerateCostsAsync(WorkScheduleEntry selected,
        CancellationToken token = default) => GenerateAndCompleteAsync(selected, requireCompleted: true, token);

    private async Task<WorkScheduleCompletionResult> GenerateAndCompleteAsync(WorkScheduleEntry selected,
        bool requireCompleted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selected);
        token.ThrowIfCancellationRequested();
        var current = await GetCurrentEntryAsync(selected, token).ConfigureAwait(false);
        if (requireCompleted && current.Status != WorkScheduleEntryStatus.Completed)
            throw new InvalidOperationException("Puoi rigenerare i costi soltanto per un intervento già finito.");

        var quote = await _data.GetQuoteByNumberAsync(current.QuoteNumber, token, includeAttachments: false)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Preventivo non trovato nello storico.");
        token.ThrowIfCancellationRequested();
        EnsureCurrentData();
        if (quote.RealProfit?.Input == null)
            throw new WorkScheduleCostsCalculationRequiredException(current.QuoteNumber);
        var company = await _data.GetCompanyAsync().WaitAsync(token).ConfigureAwait(false) ?? new Company();
        token.ThrowIfCancellationRequested();
        EnsureCurrentData();
        var context = CreatePdfContext(quote, company, _clock());

        string finalPath = _storage.BuildWorkScheduleCostsPdfPath(current.QuoteNumber, current.Date, current.Id);
        string folder = Path.GetDirectoryName(finalPath)!;
        string stagingPath = Path.Combine(folder, $".CostiLavoro_{Guid.NewGuid():N}.tmp.pdf");
        try
        {
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(folder);
                _writePdf(context, stagingPath);
                token.ThrowIfCancellationRequested();
                if (!File.Exists(stagingPath) || new FileInfo(stagingPath).Length == 0)
                    throw new IOException("Il PDF dei costi non è stato generato.");
            }, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            // The document must be ready before changing the shared state. Even a retry
            // checks the revision again: completion is idempotent for an unchanged finished
            // visit, and a visit reopened, moved or removed during rendering must not publish.
            var completed = await _schedule.CompleteEntryAsync(current.Id, current.Revision, token)
                .ConfigureAwait(false);

            // After the database commit, finish publication even if the caller closes its
            // window. A publication failure carries the committed entry for a safe retry.
            try
            {
                File.Move(stagingPath, finalPath, overwrite: true);
            }
            catch (Exception ex)
            {
                throw new WorkScheduleCostsPublicationException(completed, finalPath, ex);
            }
            return new(completed.CreateValidatedCopy(), finalPath);
        }
        finally
        {
            TryDeleteStaging(stagingPath);
        }
    }

    private async Task<WorkScheduleEntry> GetCurrentEntryAsync(WorkScheduleEntry selected, CancellationToken token)
    {
        var copy = selected.CreateValidatedCopy();
        if (copy.Kind != WorkScheduleEntryKind.Job || copy.Revision <= 0)
            throw new InvalidOperationException("Puoi completare soltanto un intervento di lavoro già salvato.");
        var snapshot = await _schedule.GetLatestAsync(copy.Date, copy.Date.AddDays(1), token).ConfigureAwait(false);
        if (!snapshot.IsCurrent)
            throw new InvalidOperationException("Il calendario non è aggiornato dal database. " +
                "Ripristina la connessione prima di completare un intervento o rigenerare i costi.");
        var current = snapshot.Entries.SingleOrDefault(entry => entry.Id == copy.Id);
        if (current == null || current.Revision != copy.Revision || current.Kind != WorkScheduleEntryKind.Job ||
            !string.Equals(current.QuoteNumber, copy.QuoteNumber, StringComparison.Ordinal))
            throw WorkScheduleRules.Conflict();
        return current.CreateValidatedCopy();
    }

    private void EnsureCurrentData()
    {
        if (!_data.CanSynchronize)
            throw new InvalidOperationException("Il database non è disponibile. " +
                "Ripristina la connessione prima di archiviare il PDF dei costi.");
    }

    internal static RealProfitPdfContext CreatePdfContext(QuoteHistoryEntry quote, Company company, DateTime generatedAt)
    {
        var saved = quote.RealProfit?.Input;
        if (saved == null || saved.Materials == null || saved.CompanyMaterials == null ||
            saved.Materials.Any(material => material == null) || saved.CompanyMaterials.Any(material => material == null))
            throw new WorkScheduleCostsCalculationRequiredException(quote.QuoteNumber);
        var input = new RealProfitInput
        {
            QuoteRevenue = saved.QuoteRevenue,
            ProfitReductionPercentage = saved.ProfitReductionPercentage,
            ExcludeMaterials = saved.ExcludeMaterials,
            SupplierDiscount = saved.SupplierDiscount,
            Workers = saved.Workers,
            Days = saved.Days,
            HoursPerDay = saved.HoursPerDay,
            HourlyCost = saved.HourlyCost,
            Materials = saved.Materials.Select(material => new ProfitMaterialCost
            {
                Name = material.Name,
                UnitOfMeasure = material.UnitOfMeasure,
                Quantity = material.Quantity,
                CustomerUnitPrice = material.CustomerUnitPrice,
                CustomerDiscount = material.CustomerDiscount
            }).ToList(),
            CompanyMaterials = saved.CompanyMaterials.Select(material => new CompanyMaterialCost
            {
                Name = material.Name,
                UnitOfMeasure = material.UnitOfMeasure,
                Quantity = material.Quantity,
                UnitCost = material.UnitCost,
                Source = material.Source
            }).ToList()
        };
        return new()
        {
            CompanyName = company.Nome,
            QuoteNumber = quote.QuoteNumber,
            QuoteDate = quote.Date == default ? generatedAt.Date : quote.Date,
            CustomerName = quote.CustomerName,
            CustomerIsSupplier = input.ExcludeMaterials,
            GeneratedAt = generatedAt,
            Input = input,
            Result = RealProfitCalculator.Calculate(input)
        };
    }

    private static void TryDeleteStaging(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[ScheduleCosts] Temporary PDF cleanup failed: {ex.GetType().Name}");
        }
    }
}
