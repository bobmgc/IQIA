using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IQIAIndicator.Core.Calibration;

/// <summary>
/// Collecteur de dataset scientifique.
/// L'identité officielle d'une observation est la combinaison :
/// SessionId + Symbol + TimeFrame + CurrentBar.
/// CurrentBar correspond à l'index réel du bar traité dans OnCalculate.
/// </summary>
public sealed class ScientificDatasetCollector
{
    private readonly Guid _sessionId;
    private readonly HashSet<ObservationIdentity> _observations = new();
    private readonly List<ScientificDatasetRecord> _records = new();

    public ScientificDatasetCollector(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("SessionId is required.", nameof(sessionId));

        _sessionId = sessionId;
    }

    public Guid SessionId => _sessionId;

    public bool IsDebugMode { get; set; }

    public IReadOnlyList<ScientificDatasetRecord> Records => _records.AsReadOnly();

    public int Count => _records.Count;

    public int TotalAddAttempts { get; private set; }

    public int RecordsAccepted { get; private set; }

    public int DuplicateRecordsRejected { get; private set; }

    public string RejectedReason { get; private set; } = string.Empty;

    public int BarsCollected => RecordsAccepted;

    public int Errors => DuplicateRecordsRejected;

    public string Status =>
        TotalAddAttempts == 0
            ? "Idle"
            : IsDebugMode
                ? "Debug"
                : "Collecting";

    public string DebugStatus =>
        $"Status: {Status}; SessionId: {_sessionId}; BarsCollected: {BarsCollected}; DuplicatesRejected: {DuplicateRecordsRejected}; Errors: {Errors}; RejectedReason: {RejectedReason}";

    public void Add(ScientificDatasetRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        TotalAddAttempts++;

        var observation = new ObservationIdentity(
            record.SessionId,
            record.Symbol,
            record.TimeFrame,
            record.CurrentBar);

        if (_observations.Contains(observation))
        {
            DuplicateRecordsRejected++;
            RejectedReason = "Duplicate observation rejected.";
        }
        else
        {
            _observations.Add(observation);
            _records.Add(record);
            RecordsAccepted++;
        }

        ValidateInvariant();
    }

    public void Clear()
    {
        _records.Clear();
        _observations.Clear();
        TotalAddAttempts = 0;
        RecordsAccepted = 0;
        DuplicateRecordsRejected = 0;
        RejectedReason = string.Empty;
    }

    public void ExportCsv(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, ToCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public void ExportJson(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, ToJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public string ToCsv()
    {
        var columns = Columns().ToArray();
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", columns.Select(Escape)));

        foreach (ScientificDatasetRecord record in _records)
        {
            builder.AppendLine(string.Join(",", columns.Select(column => Escape(Value(record, column)))));
        }

        return builder.ToString();
    }

    public string ToJson()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };
        return JsonSerializer.Serialize(_records, options);
    }

    public ScientificDatasetStatistics Describe() =>
        ScientificDatasetStatistics.Calculate(_records);

    private IEnumerable<string> Columns()
    {
        yield return "SessionId";
        yield return "Timestamp";
        yield return "Symbol";
        yield return "TimeFrame";
        yield return "CurrentPrice";
        yield return "HistoryLength";
        yield return "CurrentBar";

        foreach (string column in _records.SelectMany(record => record.Metrics.Keys).Distinct(StringComparer.Ordinal).OrderBy(value => value))
            yield return column;

        foreach (string column in _records.SelectMany(record => record.Categories.Keys).Distinct(StringComparer.Ordinal).OrderBy(value => value))
            yield return column;
    }

    private static string Value(ScientificDatasetRecord record, string column)
    {
        return column switch
        {
            "SessionId" => record.SessionId.ToString("D"),
            "Timestamp" => record.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            "Symbol" => record.Symbol,
            "TimeFrame" => record.TimeFrame,
            "CurrentPrice" => record.CurrentPrice.ToString(CultureInfo.InvariantCulture),
            "HistoryLength" => record.HistoryLength.ToString(CultureInfo.InvariantCulture),
            "CurrentBar" => record.CurrentBar.ToString(CultureInfo.InvariantCulture),
            _ when record.Metrics.TryGetValue(column, out double? metric) => metric?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            _ when record.Categories.TryGetValue(column, out string? category) => category,
            _ => string.Empty
        };
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
            ? value
            : $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private void ValidateInvariant()
    {
        if (TotalAddAttempts != RecordsAccepted + DuplicateRecordsRejected)
            throw new InvalidOperationException(
                $"Dataset collector invariant failure: TotalAddAttempts ({TotalAddAttempts}) != RecordsAccepted ({RecordsAccepted}) + DuplicateRecordsRejected ({DuplicateRecordsRejected}).");
    }

    private readonly record struct ObservationIdentity(Guid SessionId, string Symbol, string TimeFrame, int CurrentBar);
}
