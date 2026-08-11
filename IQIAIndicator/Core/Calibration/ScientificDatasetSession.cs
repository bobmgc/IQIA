using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace IQIAIndicator.Core.Calibration;

public sealed record ScientificDatasetSession(
    Guid SessionId,
    DateTime StartTime,
    DateTime? EndTime,
    string Symbol,
    string TimeFrame,
    int BarsCollected,
    string OutputDirectory,
    string? CSVPath,
    string? JSONPath,
    TimeSpan Duration,
    long DatasetSizeBytes)
{
    public double AverageMillisecondsPerBar =>
        BarsCollected == 0 ? 0.0 : Duration.TotalMilliseconds / BarsCollected;

    public string BuildReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("====================================");
        builder.AppendLine("Scientific Dataset");
        builder.AppendLine("====================================");
        builder.AppendLine($"SessionId : {SessionId}");
        builder.AppendLine($"Symbol : {Symbol}");
        builder.AppendLine($"TimeFrame : {TimeFrame}");
        builder.AppendLine($"Bars : {BarsCollected}");
        builder.AppendLine($"Duration : {Duration.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)} s");
        builder.AppendLine($"Average/bar : {AverageMillisecondsPerBar.ToString("F3", CultureInfo.InvariantCulture)} ms");
        builder.AppendLine($"OutputDirectory : {OutputDirectory}");
        builder.AppendLine($"CSV : {CSVPath ?? "N/A"}");
        builder.AppendLine($"JSON : {JSONPath ?? "N/A"}");
        builder.AppendLine($"Dataset size : {DatasetSizeBytes} bytes");
        builder.AppendLine("====================================");
        return builder.ToString();
    }
}

public sealed class ScientificDatasetSessionWriter
{
    private ScientificDatasetSession? _lastSession;

    public ScientificDatasetSession? LastSession => _lastSession;

    public ScientificDatasetSession Export(
        ScientificDatasetCollector collector,
        string outputDirectory,
        string symbol,
        string timeFrame,
        Guid sessionId,
        DateTime startTime,
        DateTime endTime)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Directory.CreateDirectory(outputDirectory);
        string safeSymbol = Sanitize(symbol, "UnknownSymbol");
        string safeTimeFrame = Sanitize(timeFrame, "UnknownTimeFrame");
        string stamp = startTime.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string prefix = $"ScientificDataset_{safeSymbol}_{safeTimeFrame}_{stamp}";
        string csvPath = Path.Combine(outputDirectory, prefix + ".csv");
        string jsonPath = Path.Combine(outputDirectory, prefix + ".json");

        collector.ExportCsv(csvPath);
        collector.ExportJson(jsonPath);

        long size = new FileInfo(csvPath).Length + new FileInfo(jsonPath).Length;
        _lastSession = new ScientificDatasetSession(
            sessionId,
            startTime,
            endTime,
            symbol,
            timeFrame,
            collector.Count,
            outputDirectory,
            csvPath,
            jsonPath,
            endTime - startTime,
            size);
        return _lastSession;
    }

    private static string Sanitize(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
            builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
        return builder.ToString().Replace(' ', '_');
    }
}
