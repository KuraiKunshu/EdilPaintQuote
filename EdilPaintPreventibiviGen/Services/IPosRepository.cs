using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public interface IPosRepository
{
    Task<List<PosWorkItem>> LoadPosTemplatesAsync(CancellationToken token = default);
    Task<PosWorkItem> SavePosTemplateAsync(PosWorkItem template, CancellationToken token = default);
    Task<PosDraft?> LoadPosDraftAsync(string quoteNumber, CancellationToken token = default);
    Task<PosDraft> SavePosDraftAsync(PosDraft draft, CancellationToken token = default);
}

/// <summary>Bound to the database shown when opening the window; no unsynchronized local writes.</summary>
public sealed class PosService(IPosRepository repository, string databaseIdentity,
    Func<string> currentIdentity, Func<CancellationToken, Task> ensureReady)
{
    public Task<List<PosWorkItem>> LoadTemplatesAsync(CancellationToken token = default) => Run(repository.LoadPosTemplatesAsync, token);
    public Task<PosWorkItem> SaveTemplateAsync(PosWorkItem template, CancellationToken token = default)
    {
        var copy = PosDraftRules.ValidateTemplate(template);
        return Run(ct => repository.SavePosTemplateAsync(copy, ct), token);
    }
    public Task<PosDraft?> LoadDraftAsync(string number, CancellationToken token = default) =>
        Run(ct => repository.LoadPosDraftAsync(number, ct), token);
    public Task<PosDraft> SaveDraftAsync(PosDraft draft, CancellationToken token = default)
    {
        var copy = PosDraftRules.ValidateDraft(draft);
        return Run(ct => repository.SavePosDraftAsync(copy, ct), token);
    }
    private Task<T> Run<T>(Func<CancellationToken, Task<T>> action, CancellationToken token) => Task.Run(async () =>
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        await DatabaseOperationCoordinator.Gate.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            CheckIdentity();
            await ensureReady(timeout.Token).ConfigureAwait(false);
            CheckIdentity();
            return await action(timeout.Token).ConfigureAwait(false);
        }
        finally { DatabaseOperationCoordinator.Gate.Release(); }
    }, CancellationToken.None);
    private void CheckIdentity()
    {
        if (databaseIdentity != currentIdentity())
            throw new InvalidOperationException("La connessione aziendale è cambiata. Riapri la finestra POS prima di continuare.");
    }
    internal static PosService ForApplication() => new(new SqlDataService(App.AppSettings),
        App.AppSettings.Database.GetCatalogIdentity(), () => App.AppSettings.Database.GetCatalogIdentity(),
        token => DatabaseOperationCoordinator.EnsureInteractiveDatabaseReadyAsync(App.DataService, "POS condiviso", token));
}
