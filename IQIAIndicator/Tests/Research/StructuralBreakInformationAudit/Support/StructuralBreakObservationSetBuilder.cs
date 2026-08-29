using System.Globalization;
using System.Net.Http;
using System.Text;
using IQIAIndicator.Backtest;
using IQIAIndicator.Core;
using IQIAIndicator.Core.MarketData;
using IQIAIndicator.Core.MarketData.Yahoo;
using IQIAIndicator.Engine.Decision.States;
using IQIAIndicator.Engine.Fusion.Core;
using IQIAIndicator.Engine.Fusion.Rules;
using IQIAIndicator.Engine.Fusion.State;
using IQIAIndicator.Engine.Risk;
using FusionEngine = IQIAIndicator.Engine.Fusion.EvidenceFusionEngine;

namespace IQIAIndicator.Tests.Research.StructuralBreakInformationAudit.Support;

/// <summary>
/// Lot 15.9. AUDIT-ONLY, single shared loader for every test class in this folder (brief §"NE CHARGE PAS
/// Yahoo 7 fois si un seul chargement partagé est plus efficace"). Loads the real Yahoo MES M5 dataset,
/// runs the real, unmodified <see cref="BacktestEngine.RunSignalPipeline"/> exactly once per test-run
/// process, then reconstructs the 6-dimension raw/stabilized Fusion values bar-by-bar via a
/// production-equivalent <see cref="FusionEngine"/> (same 5 rules, same order as
/// <see cref="BacktestEngine.RunSignalPipeline"/>) + a fresh <see cref="FusionStateManager"/>, exactly as
/// <c>StructuralBreakEvidenceLot158IntegrationTests</c> already does for the single StructuralBreak
/// dimension. Nothing here is production code - it lives entirely under Tests/Research and never touches
/// any Engine/Backtest file.
/// </summary>
internal static class StructuralBreakObservationSetBuilder
{
    public static readonly FusionDimension[] AllDimensions = Enum.GetValues<FusionDimension>();
    public static readonly MarketState[] AllRegimes = Enum.GetValues<MarketState>();

    /// <summary>Lot 15.9 brief's explicit, given list (not re-derived from EntryTriggerBuilder, which
    /// this audit deliberately does not read in detail) of MarketState values the entry pipeline currently
    /// has no IDecisionRule/EntryTrigger path for.</summary>
    public static readonly MarketState[] UnsupportedEntryRegimes =
    {
        MarketState.Trending, MarketState.RandomWalk, MarketState.StructuralBreak, MarketState.StableRange
    };

    public static readonly int StructuralBreakDimensionIndex = Array.IndexOf(AllDimensions, FusionDimension.StructuralBreak);

    public const int WarmupBars = 128;

    private static readonly Lazy<AuditDataset?> LazyDataset = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Null when Yahoo/network is unavailable (mirrors every prior Lot 15.x Yahoo test's
    /// connectivity-skip convention) - callers must treat null as "SKIPPED", never as a code failure.</summary>
    public static AuditDataset? Instance => LazyDataset.Value;

    private static InstrumentRiskSpecification Spec() => InstrumentRiskSpecification.FromInstrumentInfo(
        new InstrumentInfo("MES", TickSize: 0.25m, TickValue: 1.25m, PointValue: 5m, Decimals: 2),
        minQuantity: 1, maxQuantity: 50, quantityStep: 1);

    private static RiskPolicy Policy() => new(0.02m, null, null, null, null, null, null, null, null, null);

    private static AuditDataset? Load()
    {
        try
        {
            var source = new YahooHistoricalBarSource();
            DateTime to = DateTime.UtcNow;
            DateTime from = to.AddDays(-YahooHistoricalBarSource.DefaultMaxChunkSpanDays);

            HistoricalSeries series = source.Load("MES", "M5", from, to);
            if (series.Count == 0) return null;

            string fingerprint = HistoricalSeriesFingerprint.Compute(series);

            var scenario = BacktestScenario.Create(
                series, new BacktestWindow("LOT15.9-FULL", series.FirstTimestamp, series.LastTimestamp.AddMinutes(1)),
                50_000m, Spec(), Policy());

            BacktestSignalPipelineResult signalResult = new BacktestEngine().RunSignalPipeline(scenario, WarmupBars);

            List<ObservationRecord> observations = BuildObservations(signalResult, series.Symbol, series.TimeFrame);

            return new AuditDataset(series, fingerprint, signalResult, observations);
        }
        catch (Exception exception) when (IsConnectivityOrProviderIssue(exception))
        {
            return null;
        }
    }

    /// <summary>Public so the determinism check (brief Section A / §21: build the observation set twice
    /// from the same inputs and compare a hash) can rebuild it a second time from the already-computed
    /// <see cref="BacktestSignalPipelineResult"/>, with fresh Fusion engine/state objects, without a second
    /// network call.</summary>
    public static List<ObservationRecord> BuildObservations(BacktestSignalPipelineResult signalResult, string symbol, string timeFrame)
    {
        var fusionEngine = new FusionEngine(new IFusionRule[]
        {
            new StationarityRule(), new PersistenceRule(), new MeanReversionRule(), new RandomWalkRule(),
            new StructuralBreakEvidenceRule()
        });
        var fusionState = new FusionStateManager();
        var list = new List<ObservationRecord>(signalResult.Bars.Count);

        foreach (BacktestSignalResult bar in signalResult.Bars)
        {
            if (bar.Status is BacktestSignalStatus.Rejected or BacktestSignalStatus.Exception) continue;
            if (bar.Regime is null || bar.Decision is null) continue;

            StructuralBreakContract contract = StructuralBreakEvidenceRule.BuildContract(bar.Regime.Cusum, bar.Regime.BaiPerron);

            FusionResult raw = fusionEngine.Fuse(new FusionContext
            {
                Evidence = bar.Regime, Timestamp = bar.Timestamp, Symbol = symbol, TimeFrame = timeFrame, EvaluationId = Guid.Empty
            });
            FusionSnapshot snap = fusionState.Update(raw, bar.Timestamp);

            var rawValue = new double[AllDimensions.Length];
            var rawAvail = new bool[AllDimensions.Length];
            var stableValue = new double[AllDimensions.Length];
            var stableAvail = new bool[AllDimensions.Length];

            for (int i = 0; i < AllDimensions.Length; i++)
            {
                FusionDimension dim = AllDimensions[i];

                // StructuralStability is never produced by this 5-rule FusionEngine - it is computed
                // separately, post-hoc, inside FusionStateManager.Update itself (see its own
                // ReplaceStructuralStability). Its "raw" slot therefore has no genuine pre-stabilization
                // measurement; we use the exact same sentinel FusionStateManager's own private
                // GetConfidence fallback uses (Value=0.0, IsAvailable=false) instead of throwing on the
                // missing dictionary key or inventing a different convention.
                if (raw.Dimensions.TryGetValue(dim, out FusionConfidence? rc))
                {
                    rawValue[i] = rc.Value;
                    rawAvail[i] = rc.IsAvailable;
                }
                else
                {
                    rawValue[i] = 0.0;
                    rawAvail[i] = false;
                }

                FusionConfidence sc = snap.StableResult.Dimensions[dim];
                stableValue[i] = sc.Value;
                stableAvail[i] = sc.IsAvailable;
            }

            list.Add(new ObservationRecord
            {
                BarIndex = bar.BarIndex,
                Timestamp = bar.Timestamp,
                Winner = bar.Decision.Winner,
                Cusum = bar.Regime.Cusum,
                BaiPerron = bar.Regime.BaiPerron,
                Contract = contract,
                RawValue = rawValue,
                RawAvailable = rawAvail,
                StableValue = stableValue,
                StableAvailable = stableAvail
            });
        }

        return list;
    }

    /// <summary>Bit-identical determinism hash over every raw field this builder reads/derives, per bar,
    /// in order. Two independent calls to <see cref="BuildObservations"/> over the same
    /// <see cref="BacktestSignalPipelineResult"/> must produce the same hash (brief §21).</summary>
    public static string HashObservations(IReadOnlyList<ObservationRecord> observations)
    {
        var sb = new StringBuilder();
        foreach (ObservationRecord o in observations)
        {
            sb.Append(o.BarIndex).Append('|')
              .Append(o.Timestamp.ToString("O", CultureInfo.InvariantCulture)).Append('|')
              .Append(o.Winner).Append('|')
              .Append(o.Cusum?.IsValid.ToString() ?? "null").Append('|')
              .Append(o.Cusum?.ChangeDetected.ToString() ?? "null").Append('|')
              .Append(o.Cusum is null ? "null" : o.Cusum.Confidence.ToString("G17", CultureInfo.InvariantCulture)).Append('|')
              .Append(o.Cusum is null ? "null" : o.Cusum.PositiveCusum.ToString("G17", CultureInfo.InvariantCulture)).Append('|')
              .Append(o.Cusum is null ? "null" : o.Cusum.NegativeCusum.ToString("G17", CultureInfo.InvariantCulture)).Append('|')
              .Append(o.Cusum is null ? "null" : o.Cusum.Threshold.ToString("G17", CultureInfo.InvariantCulture)).Append('|')
              .Append(o.Cusum?.SampleSize.ToString() ?? "null").Append('|')
              .Append(o.Cusum?.EstimatedBreakIndex.ToString() ?? "null").Append('|')
              .Append(o.BaiPerron?.IsValid.ToString() ?? "null").Append('|')
              .Append(o.BaiPerron?.BreakCount.ToString() ?? "null").Append('|')
              .Append(o.Contract.Detected).Append('|')
              .Append(o.Contract.Strength.ToString("G17", CultureInfo.InvariantCulture)).Append('|')
              .Append(o.Contract.Agreement).Append('|')
              .Append(o.Contract.IsAvailable).Append('|');

            for (int i = 0; i < o.RawValue.Length; i++)
                sb.Append(o.RawValue[i].ToString("G17", CultureInfo.InvariantCulture)).Append('/').Append(o.RawAvailable[i]).Append(',');
            for (int i = 0; i < o.StableValue.Length; i++)
                sb.Append(o.StableValue[i].ToString("G17", CultureInfo.InvariantCulture)).Append('/').Append(o.StableAvailable[i]).Append(',');

            sb.Append(';');
        }

        return AuditCsv.Sha256Hex(sb.ToString());
    }

    private static bool IsConnectivityOrProviderIssue(Exception exception) =>
        exception is global::IQIAIndicator.Core.MarketData.Yahoo.YahooProviderException or HttpRequestException or TaskCanceledException;
}
