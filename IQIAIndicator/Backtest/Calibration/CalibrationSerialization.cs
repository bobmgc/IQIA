using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §44). Minimal, plain-JSON export/import for calibration results - brief
/// §44: "NE PAS créer un système de base de données complexe. JSON ou modèle sérialisable suffisant." No
/// bespoke binary format, no custom converters beyond making enums human-readable
/// (<see cref="JsonStringEnumConverter"/>) - every type on the export path
/// (<see cref="CalibrationExperimentResult"/>/<see cref="CalibrationWindow"/>/dictionaries/lists of
/// primitives) already round-trips through <see cref="System.Text.Json"/>'s own default constructor/
/// property-matching support, verified by <c>CalibrationSerializationTests</c>.
/// </summary>
public static class CalibrationSerialization
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ToJson(CalibrationExperimentRunResult runResult) => JsonSerializer.Serialize(runResult, Options);

    public static CalibrationExperimentRunResult FromJson(string json) =>
        JsonSerializer.Deserialize<CalibrationExperimentRunResult>(json, Options)
        ?? throw new JsonException("Deserialized CalibrationExperimentRunResult was null.");

    public static string ToJson(IReadOnlyList<CalibrationExperimentResult> results) => JsonSerializer.Serialize(results, Options);

    public static IReadOnlyList<CalibrationExperimentResult> ResultsFromJson(string json) =>
        JsonSerializer.Deserialize<IReadOnlyList<CalibrationExperimentResult>>(json, Options)
        ?? throw new JsonException("Deserialized CalibrationExperimentResult list was null.");
}
