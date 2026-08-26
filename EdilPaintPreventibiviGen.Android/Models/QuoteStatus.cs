namespace EdilPaintPreventibiviGen.Android.Models;

public enum QuoteStatus
{
    Finalizzato,
    Spedito,
    Confermato,
    Finito,
    Rifiutato,
    Bozza,
    DaInviare,
    DaSollecitare,
    Archiviato
}

public sealed record QuoteStatusOption(string Label, QuoteStatus? Value)
{
    public override string ToString() => Label;
}

public static class QuoteStatusOptions
{
    public static IReadOnlyList<QuoteStatusOption> All { get; } =
    [
        new("Tutti gli stati", null),
        new("Finalizzato", QuoteStatus.Finalizzato),
        new("Spedito", QuoteStatus.Spedito),
        new("Confermato", QuoteStatus.Confermato),
        new("Finito", QuoteStatus.Finito),
        new("Rifiutato", QuoteStatus.Rifiutato),
        new("Bozza", QuoteStatus.Bozza),
        new("Da inviare", QuoteStatus.DaInviare),
        new("Da sollecitare", QuoteStatus.DaSollecitare),
        new("Archiviato", QuoteStatus.Archiviato)
    ];

    public static IReadOnlyList<QuoteStatusOption> Editable { get; } = All.Skip(1).ToList();
}
