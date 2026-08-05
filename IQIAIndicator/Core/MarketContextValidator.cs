namespace IQIAIndicator.Core;

/// <summary>
/// Vérifie qu'un MarketContext est exploitable avant transmission aux moteurs.
/// Ne corrige aucune donnée — signale uniquement les anomalies.
/// </summary>
public sealed class MarketContextValidator
{
    public ValidationResult Validate(MarketContext context)
    {
        var errors   = new List<string>();
        var warnings = new List<string>();

        CheckPrice(context.Price,      errors, warnings);
        CheckVolume(context.Volume,    errors);
        CheckInstrument(context.Instrument, errors, warnings);
        CheckClock(context.Clock,      errors);

        return new ValidationResult
        {
            IsValid  = errors.Count == 0,
            Errors   = errors,
            Warnings = warnings
        };
    }

    // ─── Contrôles prix ──────────────────────────────────────────────────────

    private static void CheckPrice(
        PriceInfo p, List<string> errors, List<string> warnings)
    {
        if (p.Open  <= 0m) errors.Add($"Open invalide : {p.Open}.");
        if (p.Close <= 0m) errors.Add($"Close invalide : {p.Close}.");
        if (p.High  < p.Low)
            errors.Add($"High ({p.High}) inférieur à Low ({p.Low}).");
        if (p.High  < p.Close)
            warnings.Add($"High ({p.High}) < Close ({p.Close}) : anomalie de clôture.");
        if (p.Low   > p.Close)
            warnings.Add($"Low ({p.Low}) > Close ({p.Close}) : anomalie de clôture.");
    }

    // ─── Contrôles volume ────────────────────────────────────────────────────

    private static void CheckVolume(VolumeInfo v, List<string> errors)
    {
        if (v.Volume < 0m) errors.Add($"Volume négatif : {v.Volume}.");
    }

    // ─── Contrôles instrument ────────────────────────────────────────────────

    private static void CheckInstrument(
        InstrumentInfo i, List<string> errors, List<string> warnings)
    {
        if (i.TickSize <= 0m)
            errors.Add($"TickSize invalide : {i.TickSize}.");
        if (string.IsNullOrWhiteSpace(i.Symbol))
            warnings.Add("Symbol non renseigné.");
    }

    // ─── Contrôles horloge ───────────────────────────────────────────────────

    private static void CheckClock(MarketClock c, List<string> errors)
    {
        if (c.CurrentTime == default)
            errors.Add("Heure du bar non renseignée (DateTime.MinValue).");
    }
}
