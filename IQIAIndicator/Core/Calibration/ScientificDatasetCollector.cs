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
/// L'identité logique d'une observation est la combinaison :
/// SessionId + Symbol + TimeFrame + CurrentBar.
/// CurrentBar correspond à l'index réel du bar traité dans OnCalculate.
///
/// Sprint 15.17 (QDE-012 real-market capture): Add() validates OHLCV structural integrity
/// (High/Low/Open/Close/Volume consistency) before anything else. Records rejected for invalid
/// OHLCV never occupy a bar's identity slot, so a later, valid record for the same bar can still be
/// accepted.
///
/// Sprint 15.19 (QDE-012 forming-bar-capture fix): a real ATAS capture analyzed in Sprint 15.18 proved
/// that ATAS can invoke OnCalculate many times for the SAME still-forming bar (one isolated capture
/// showed 71 calls for one bar across ~43 real seconds; the primary capture showed a contiguous
/// 633-bar / 36.5% tail where every committed "bar" was a single-tick, Volume=1, zero-range snapshot).
/// The PRE-15.19 dedup policy ("first Add() wins", reject every later call for the same CurrentBar as
/// a duplicate) therefore committed whichever snapshot arrived FIRST during a bar's formation - often
/// the least complete one - not the bar's final, closed state. There is no explicit "bar closed"
/// signal anywhere in the installed ATAS SDK (ATAS.Indicators.dll/ATAS.DataFeedsCore.dll/ATAS.Types.dll,
/// confirmed by reflection - see QDE-012_Sprint_15.19 report §3); the only reliable evidence that a bar
/// has closed is that OnCalculate has moved on to processing a LATER bar index. This class now buffers
/// the most-recently-seen observation for the CURRENT bar ("pending") and only commits it to the
/// exported dataset once a callback for a later bar proves it is closed ("finalized") - "last update
/// before advance wins", not "first Add() wins". See the report for the full state-machine rationale.
/// </summary>
public sealed class ScientificDatasetCollector
{
    private readonly Guid _sessionId;
    private readonly List<ScientificDatasetRecord> _records = new();

    // Sprint 15.19: the single forming bar currently being observed, if any. Replaced (not
    // discarded) on every further callback for the same bar identity - see Add(). Committed to
    // _records only when a callback for a later bar proves this one has closed (CommitPending), or
    // intentionally EXCLUDED at session end (Flush) if it never advanced past - see class doc comment.
    private ScientificDatasetRecord? _pendingBar;

    // Sprint 15.19: the last record actually committed to _records - used to distinguish a genuine
    // re-delivery of already-closed data (BarsDuplicated) from an ambiguous rewind that is merely
    // behind the current pending bar (RecordsOutOfOrder). Never mutated once assigned other than by
    // CommitPending()/Clear().
    private ScientificDatasetRecord? _lastFinalized;

    private bool _flushed;

    public ScientificDatasetCollector(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("SessionId is required.", nameof(sessionId));

        _sessionId = sessionId;
    }

    public Guid SessionId => _sessionId;

    public bool IsDebugMode { get; set; }

    /// <summary>Finalized (closed-bar) observations only - never includes the current forming bar.
    /// This is the exact set persisted by ExportCsv()/ExportJson()/ExportOhlcvCsv().</summary>
    public IReadOnlyList<ScientificDatasetRecord> Records => _records.AsReadOnly();

    public int Count => _records.Count;

    public int TotalAddAttempts { get; private set; }

    /// <summary>Sprint 15.19: bars actually committed to the dataset - i.e. bars ATAS has proven
    /// closed by advancing past them (or, pre-15.19, "first snapshot accepted" - see class doc
    /// comment for why that was unsafe). Kept under its original Sprint 15.17 name for source
    /// compatibility with DatasetDashboard/ScientificCollectionMonitorWidget/SystemHealthAggregator,
    /// none of which this sprint's allowed-file list permits editing; BarsFinalized is the same
    /// counter under its Sprint 15.19 name.</summary>
    public int RecordsAccepted { get; private set; }

    /// <summary>Sprint 15.19 alias of RecordsAccepted - see that property's doc comment.</summary>
    public int BarsFinalized => RecordsAccepted;

    /// <summary>Sprint 15.19: a callback whose (Symbol, TimeFrame, CurrentBar) matches an
    /// ALREADY-FINALIZED (closed, immutable) bar - genuine re-delivery of settled data, e.g. an ATAS
    /// Market Replay rewind re-visiting a bar it already closed. Never a legitimate still-forming
    /// update (those are FormingBarUpdates, not this). Kept under its original Sprint 15.17 name
    /// ("duplicate") for the same source-compatibility reason as RecordsAccepted above - see
    /// BarsDuplicated for the Sprint 15.19 name.</summary>
    public int DuplicateRecordsRejected { get; private set; }

    /// <summary>Sprint 15.19 alias of DuplicateRecordsRejected - see that property's doc comment.</summary>
    public int BarsDuplicated => DuplicateRecordsRejected;

    public int InvalidRecordsRejected { get; private set; }

    /// <summary>Sprint 15.19: a callback whose CurrentBar rewinds behind the currently pending bar
    /// but does not match any already-finalized bar - an ambiguous rewind, counted but never applied
    /// (the pending bar is left untouched), matching this class's pre-15.19 "never silently drop
    /// legitimate data merely for being out of order" philosophy. Kept under its original Sprint
    /// 15.17 name for source compatibility - see BarsOutOfOrder for the Sprint 15.19 name.</summary>
    public int RecordsOutOfOrder { get; private set; }

    /// <summary>Sprint 15.19 alias of RecordsOutOfOrder - see that property's doc comment.</summary>
    public int BarsOutOfOrder => RecordsOutOfOrder;

    /// <summary>Sprint 15.19 (new): a valid callback whose (Symbol, TimeFrame, CurrentBar) matches
    /// the CURRENTLY PENDING (still-forming) bar exactly - i.e. ATAS invoked OnCalculate again for a
    /// bar that has not yet been proven closed. The pending snapshot is replaced with this call's
    /// data (latest-update-wins), never rejected. This is the counter that used to be silently folded
    /// into "duplicate" pre-Sprint-15.19 - see class doc comment and QDE-012_Sprint_15.18 report §6.</summary>
    public int FormingBarUpdates { get; private set; }

    /// <summary>Sprint 15.19 (new): 1 if a bar was still pending (not yet proven closed) when Flush()
    /// was called, 0 otherwise. That bar is deliberately EXCLUDED from the finalized dataset - see
    /// Flush() and the class doc comment's EXCLUDE_CURRENT_FORMING_BAR policy.</summary>
    public int BarsPendingAtDispose => _flushed && _pendingBar is not null ? 1 : 0;

    /// <summary>Sprint 15.19 (new): the bar currently being observed but not yet proven closed, or
    /// null if none has been seen yet this session. Exposed read-only for diagnostics/tests; never
    /// part of Records/exports until (if ever) it is finalized.</summary>
    public ScientificDatasetRecord? PendingBar => _pendingBar;

    /// <summary>Sprint 15.19 (new): wall-clock time (UtcNow) of the first callback that started the
    /// CURRENT pending bar. Reset every time a new bar becomes pending. Diagnostic only - proves,
    /// independently of the OHLCV payload itself, that a bar received callbacks spread over real
    /// time (see QDE-012_Sprint_15.18 report §6, session 165335: 71 callbacks across ~43s).</summary>
    public DateTime? PendingBarFirstSeenAt { get; private set; }

    /// <summary>Sprint 15.19 (new): wall-clock time (UtcNow) of the most recent callback for the
    /// CURRENT pending bar (may equal PendingBarFirstSeenAt if only one callback has arrived so far).</summary>
    public DateTime? PendingBarLastSeenAt { get; private set; }

    public DateTime? FirstTimestamp { get; private set; }

    public DateTime? LastTimestamp { get; private set; }

    public string RejectedReason { get; private set; } = string.Empty;

    public int BarsCollected => RecordsAccepted;

    public int Errors => DuplicateRecordsRejected + InvalidRecordsRejected;

    public string Status =>
        TotalAddAttempts == 0
            ? "Idle"
            : IsDebugMode
                ? "Debug"
                : "Collecting";

    public string DebugStatus =>
        $"Status: {Status}; SessionId: {_sessionId}; BarsCollected: {BarsCollected}; DuplicatesRejected: {DuplicateRecordsRejected}; Errors: {Errors}; RejectedReason: {RejectedReason}";

    /// <summary>
    /// Sprint 15.19 state machine. Every call falls into exactly one bucket:
    ///   1. structurally invalid  -> InvalidRecordsRejected++, pending bar untouched
    ///   2. no bar pending yet    -> becomes the new pending bar
    ///   3. same bar as pending   -> FormingBarUpdates++, pending REPLACED (latest wins)
    ///   4. bar advanced (same Symbol/TimeFrame, higher CurrentBar), or Symbol/TimeFrame changed
    ///      -> the OLD pending bar is proven closed: CommitPending(), then this call starts the new pending bar
    ///   5. rewind matching an already-finalized bar -> DuplicateRecordsRejected++ (immutable, not reapplied)
    ///   6. any other rewind (behind pending, ahead of last finalized) -> RecordsOutOfOrder++ (counted, not applied)
    /// See the class doc comment for why (1) is checked before the pending-bar identity at all, and
    /// QDE-012_Sprint_15.19 report §5/§6 for the full state-transition table and edge-case rationale.
    /// </summary>
    public void Add(ScientificDatasetRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        TotalAddAttempts++;

        if (!TryValidateOhlcv(record, out string invalidReason))
        {
            InvalidRecordsRejected++;
            RejectedReason = invalidReason;
            ValidateInvariant();
            return;
        }

        if (_pendingBar is null)
        {
            StartPending(record);
            ValidateInvariant();
            return;
        }

        if (SameBar(record, _pendingBar))
        {
            FormingBarUpdates++;
            _pendingBar = record;
            PendingBarLastSeenAt = DateTime.UtcNow;
            ValidateInvariant();
            return;
        }

        bool sameStreamAsPending = SameStream(record, _pendingBar);
        bool isAdvance = sameStreamAsPending && record.CurrentBar > _pendingBar.CurrentBar;

        if (isAdvance || !sameStreamAsPending)
        {
            // Bar advanced within the same stream, OR Symbol/TimeFrame changed mid-session (Sprint
            // 15.19 edge case I: never blend two instrument/timeframe streams into one pending slot).
            // Either way the OLD pending bar is now proven closed.
            CommitPending();
            StartPending(record);
            ValidateInvariant();
            return;
        }

        // record.CurrentBar < _pendingBar.CurrentBar, same stream: a rewind. Classify against the
        // last-FINALIZED bar (immutable once committed), not the still-pending one.
        if (_lastFinalized is not null && SameStream(record, _lastFinalized) && record.CurrentBar <= _lastFinalized.CurrentBar)
        {
            // Genuine re-delivery of already-closed data (e.g. an ATAS Market Replay rewind
            // revisiting a bar it already finalized). The finalized record is immutable - never
            // reapplied.
            DuplicateRecordsRejected++;
            RejectedReason = "Duplicate observation rejected: bar already finalized.";
            ValidateInvariant();
            return;
        }

        // Ahead of the last finalized bar (or nothing finalized yet) but behind the current pending
        // bar: an ambiguous rewind. Counted, never applied - matches this class's pre-15.19 "never
        // silently drop legitimate data merely for being out of order" philosophy; the pending bar is
        // left exactly as it was.
        RecordsOutOfOrder++;
        ValidateInvariant();
    }

    /// <summary>
    /// Sprint 15.19: call exactly once, at session end (IQIAIndicator.OnDispose(), before export),
    /// never mid-session. If a bar is still pending (never proven closed by a later callback), it is
    /// deliberately EXCLUDED from the finalized dataset - policy EXCLUDE_CURRENT_FORMING_BAR, chosen
    /// because there is no ATAS-SDK evidence available at dispose time that a still-pending bar has
    /// actually closed (see class doc comment). BarsPendingAtDispose reflects this afterward; the
    /// excluded bar's last-seen snapshot remains inspectable via PendingBar for diagnostics. Safe to
    /// call more than once (idempotent) and safe to call on a collector with nothing pending.
    /// </summary>
    public void Flush() => _flushed = true;

    private void StartPending(ScientificDatasetRecord record)
    {
        _pendingBar = record;
        PendingBarFirstSeenAt = DateTime.UtcNow;
        PendingBarLastSeenAt = PendingBarFirstSeenAt;
    }

    private void CommitPending()
    {
        ScientificDatasetRecord finalized = _pendingBar!;
        _records.Add(finalized);
        _lastFinalized = finalized;
        RecordsAccepted++;

        FirstTimestamp ??= finalized.Timestamp;
        if (LastTimestamp is null || finalized.Timestamp > LastTimestamp)
            LastTimestamp = finalized.Timestamp;
    }

    private static bool SameStream(ScientificDatasetRecord a, ScientificDatasetRecord b) =>
        string.Equals(a.Symbol, b.Symbol, StringComparison.Ordinal)
        && string.Equals(a.TimeFrame, b.TimeFrame, StringComparison.Ordinal);

    private static bool SameBar(ScientificDatasetRecord a, ScientificDatasetRecord b) =>
        SameStream(a, b) && a.CurrentBar == b.CurrentBar;

    /// <summary>Structural OHLCV validity only - never rejects on a value simply being unusual
    /// (e.g. zero volume, which some feeds/instruments legitimately never report).
    ///
    /// Open/High/Low all being exactly 0 is treated as "OHLCV not supplied" and skips the
    /// High/Low/Open/Close consistency checks below - this is what every ScientificDatasetRecord
    /// constructed before Sprint 15.17 looks like (Open/High/Low default to 0m; only CurrentPrice/
    /// Close was ever populated), and pre-existing test fixtures still construct records this way. A
    /// real OHLCV record from ATAS never has Open/High/Low all equal to 0 for any traded instrument,
    /// so this cannot mask a real invalid observation in practice.</summary>
    private static bool TryValidateOhlcv(ScientificDatasetRecord record, out string reason)
    {
        if (record.Timestamp == default)
        {
            reason = "Invalid record: Timestamp is unset (default DateTime).";
            return false;
        }

        bool ohlcSupplied = record.Open != 0m || record.High != 0m || record.Low != 0m;
        if (!ohlcSupplied)
        {
            reason = string.Empty;
            return true;
        }

        if (record.High < record.Low)
        {
            reason = $"Invalid record: High ({record.High}) < Low ({record.Low}).";
            return false;
        }

        decimal maxOpenClose = Math.Max(record.Open, record.Close);
        decimal minOpenClose = Math.Min(record.Open, record.Close);
        if (record.High < maxOpenClose)
        {
            reason = $"Invalid record: High ({record.High}) < max(Open, Close) ({maxOpenClose}).";
            return false;
        }

        if (record.Low > minOpenClose)
        {
            reason = $"Invalid record: Low ({record.Low}) > min(Open, Close) ({minOpenClose}).";
            return false;
        }

        if (record.Volume < 0m)
        {
            reason = $"Invalid record: Volume ({record.Volume}) is negative.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public void Clear()
    {
        _records.Clear();
        _pendingBar = null;
        _lastFinalized = null;
        _flushed = false;
        TotalAddAttempts = 0;
        RecordsAccepted = 0;
        DuplicateRecordsRejected = 0;
        InvalidRecordsRejected = 0;
        RecordsOutOfOrder = 0;
        FormingBarUpdates = 0;
        PendingBarFirstSeenAt = null;
        PendingBarLastSeenAt = null;
        FirstTimestamp = null;
        LastTimestamp = null;
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

    /// <summary>Sprint 15.17: the minimal, QDE-012-facing OHLCV dataset - SessionId, Timestamp,
    /// Symbol, TimeFrame, Open, High, Low, Close, Volume, CurrentBar only. No scientific
    /// Metrics/Categories columns - those remain in ToCsv()/ToJson() (unchanged, still exported
    /// alongside this). Close is the record's CurrentPrice under its OHLCV name (see
    /// ScientificDatasetRecord.Close).</summary>
    public string ToOhlcvCsv()
    {
        string[] columns = { "SessionId", "Timestamp", "Symbol", "TimeFrame", "Open", "High", "Low", "Close", "Volume", "CurrentBar" };
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", columns));

        foreach (ScientificDatasetRecord record in _records)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                record.SessionId.ToString("D"),
                record.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                Escape(record.Symbol),
                Escape(record.TimeFrame),
                record.Open.ToString(CultureInfo.InvariantCulture),
                record.High.ToString(CultureInfo.InvariantCulture),
                record.Low.ToString(CultureInfo.InvariantCulture),
                record.Close.ToString(CultureInfo.InvariantCulture),
                record.Volume.ToString(CultureInfo.InvariantCulture),
                record.CurrentBar.ToString(CultureInfo.InvariantCulture)
            }));
        }

        return builder.ToString();
    }

    public void ExportOhlcvCsv(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, ToOhlcvCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private IEnumerable<string> Columns()
    {
        yield return "SessionId";
        yield return "Timestamp";
        yield return "Symbol";
        yield return "TimeFrame";
        yield return "CurrentPrice";
        yield return "Open";
        yield return "High";
        yield return "Low";
        yield return "Volume";
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
            "Open" => record.Open.ToString(CultureInfo.InvariantCulture),
            "High" => record.High.ToString(CultureInfo.InvariantCulture),
            "Low" => record.Low.ToString(CultureInfo.InvariantCulture),
            "Volume" => record.Volume.ToString(CultureInfo.InvariantCulture),
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

    /// <summary>Sprint 15.19: every Add() call is accounted for in exactly one bucket -
    /// InvalidRecordsRejected, DuplicateRecordsRejected, RecordsOutOfOrder, FormingBarUpdates, or "this
    /// call started a new pending bar". The last bucket isn't independently counted (it coincides
    /// exactly with committing the OLD pending bar, i.e. RecordsAccepted, except for the very first
    /// bar of the session or after Clear(), which starts a pending bar without committing anything) -
    /// captured below by the "+ (_pendingBar is not null ? 1 : 0)" term, which is 1 from the moment
    /// any bar has ever been pending onward (StartPending always runs before this check on the same
    /// call) and stays 1 through Flush() (Flush() never nulls _pendingBar - see its doc comment).</summary>
    private void ValidateInvariant()
    {
        int accountedFor = InvalidRecordsRejected + DuplicateRecordsRejected + RecordsOutOfOrder
            + FormingBarUpdates + RecordsAccepted + (_pendingBar is not null ? 1 : 0);

        if (TotalAddAttempts != accountedFor)
            throw new InvalidOperationException(
                $"Dataset collector invariant failure: TotalAddAttempts ({TotalAddAttempts}) != " +
                $"InvalidRecordsRejected ({InvalidRecordsRejected}) + DuplicateRecordsRejected ({DuplicateRecordsRejected}) + " +
                $"RecordsOutOfOrder ({RecordsOutOfOrder}) + FormingBarUpdates ({FormingBarUpdates}) + " +
                $"RecordsAccepted ({RecordsAccepted}) + pendingBarStarted ({(_pendingBar is not null ? 1 : 0)}) = {accountedFor}.");
    }
}
