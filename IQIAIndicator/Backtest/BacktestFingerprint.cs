using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IQIAIndicator.Core;
using IQIAIndicator.Engine.Regime.Core;
using IQIAIndicator.Engine.Regime.Evidence;
using IQIAIndicator.Engine.Regime.Evidence.ADF;
using IQIAIndicator.Engine.Regime.Evidence.BaiPerron;
using IQIAIndicator.Engine.Regime.Evidence.CUSUM;
using IQIAIndicator.Engine.Regime.Evidence.DFA;
using IQIAIndicator.Engine.Regime.Evidence.HalfLife;
using IQIAIndicator.Engine.Regime.Evidence.KPSS;
using IQIAIndicator.Engine.Regime.Evidence.VarianceRatio;
using IQIAIndicator.Engine.Risk;

namespace IQIAIndicator.Backtest;

/// <summary>
/// Sprint 15.25 (Lot 14.1, brief §17). Deterministic, wall-clock-free canonicalization used to build
/// <see cref="BacktestFoundationResult.ScenarioId"/> and <see cref="BacktestFoundationResult.DeterministicHash"/>.
///
/// Every formatting choice here is deliberate: decimals and doubles are rendered with
/// <see cref="CultureInfo.InvariantCulture"/> (default .NET formatting since .NET Core 3.0 is already
/// round-trippable/shortest-round-trip, so no precision is lost), booleans render as their invariant
/// "True"/"False", and DateTime uses the round-trip "O" format. No field here is ever a GUID, a random
/// number, or <c>DateTime.UtcNow</c> - only values already present in the scenario/bar/evidence being
/// fingerprinted.
///
/// Evidence fingerprinting deliberately hashes every NUMERIC/boolean field of each of the nine
/// <see cref="EvidenceSet"/> members, but not their free-text <c>Explanation</c> (derived, human-readable
/// text with no information the numeric fields do not already carry) and not DFA's diagnostic
/// <c>WindowSizes</c>/<c>Fluctuations</c> series (large, purely illustrative arrays - DFA's substantive
/// result is already captured by Hurst/RSquared/Confidence/WindowCount/IsValid). This scoping is
/// intentional, not an oversight.
/// </summary>
internal static class BacktestFingerprint
{
    public static string ComputeScenarioId(BacktestScenario scenario)
    {
        var sb = new StringBuilder();
        sb.Append("Series.Symbol=").Append(scenario.Series.Symbol).Append('|');
        sb.Append("Series.TimeFrame=").Append(scenario.Series.TimeFrame).Append('|');
        sb.Append("Series.TimeZone=").Append(scenario.Series.TimeZone).Append('|');
        sb.Append("Series.Provider=").Append(scenario.Series.Provider).Append('|');
        sb.Append("Series.Count=").Append(scenario.Series.Count).Append('|');
        sb.Append("Series.First=").Append(Dt(scenario.Series.FirstTimestamp)).Append('|');
        sb.Append("Series.Last=").Append(Dt(scenario.Series.LastTimestamp)).Append('|');
        sb.Append("Window.Name=").Append(scenario.Window.Name).Append('|');
        sb.Append("Window.From=").Append(Dt(scenario.Window.From)).Append('|');
        sb.Append("Window.To=").Append(Dt(scenario.Window.To)).Append('|');
        sb.Append("InitialCapital=").Append(D(scenario.InitialCapital)).Append('|');
        AppendInstrument(sb, scenario.Instrument);
        AppendPolicy(sb, scenario.Policy);

        return Sha256Hex(sb.ToString());
    }

    /// <summary>Appends one bar's canonical fingerprint line to <paramref name="accumulator"/>. Call once
    /// per PROCESSED bar, in chronological order (the order is itself part of what the hash proves).</summary>
    public static void AppendBar(StringBuilder accumulator, int index, bool isWarmup, MarketContext context, EvidenceSet evidence)
    {
        accumulator.Append("Bar[").Append(index).Append("] warmup=").Append(isWarmup);
        accumulator.Append(" ts=").Append(Dt(context.Clock.CurrentTime));
        accumulator.Append(" O=").Append(D(context.Price.Open));
        accumulator.Append(" H=").Append(D(context.Price.High));
        accumulator.Append(" L=").Append(D(context.Price.Low));
        accumulator.Append(" C=").Append(D(context.Price.Close));
        accumulator.Append(" Med=").Append(D(context.Price.Median));
        accumulator.Append(" Typ=").Append(D(context.Price.TypicalPrice));
        accumulator.Append(" Vol=").Append(D(context.Volume.Volume));
        accumulator.Append(" IsFirstBar=").Append(context.Clock.IsFirstBar);
        accumulator.Append(" IsLastBar=").Append(context.Clock.IsLastBar);
        accumulator.Append(" ElapsedMin=").Append(context.Clock.ElapsedMinutes);
        accumulator.Append(" CurrentBar=").Append(context.Execution.CurrentBar);
        accumulator.Append(" IsRealtime=").Append(context.Execution.IsRealtime);
        accumulator.Append(" IsHistorical=").Append(context.Execution.IsHistorical);
        accumulator.Append(" IsReplay=").Append(context.Execution.IsReplay);

        AppendAdf(accumulator, evidence.Adf);
        AppendKpss(accumulator, evidence.Kpss);
        AppendHurst(accumulator, evidence.Hurst);
        AppendHalfLife(accumulator, evidence.HalfLife);
        AppendVarianceRatio(accumulator, evidence.VarianceRatio);
        AppendCusum(accumulator, evidence.Cusum);
        AppendVolatility(accumulator, evidence.Volatility);
        AppendBaiPerron(accumulator, evidence.BaiPerron);
        AppendDfa(accumulator, evidence.Dfa);

        accumulator.Append('\n');
    }

    public static string Sha256Hex(string canonical)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(canonical);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    // ── Formatting primitives ────────────────────────────────────────────────────────────────────────

    private static string D(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string D(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Dt(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    // ── Instrument / policy ──────────────────────────────────────────────────────────────────────────

    private static void AppendInstrument(StringBuilder sb, InstrumentRiskSpecification instrument)
    {
        sb.Append("Instrument.Symbol=").Append(instrument.Symbol).Append('|');
        sb.Append("Instrument.TickSize=").Append(D(instrument.TickSize)).Append('|');
        sb.Append("Instrument.TickValue=").Append(D(instrument.TickValue)).Append('|');
        sb.Append("Instrument.PointValue=").Append(D(instrument.PointValue)).Append('|');
        sb.Append("Instrument.MinQuantity=").Append(instrument.MinQuantity).Append('|');
        sb.Append("Instrument.MaxQuantity=").Append(instrument.MaxQuantity).Append('|');
        sb.Append("Instrument.QuantityStep=").Append(instrument.QuantityStep).Append('|');
        sb.Append("Instrument.ContractMultiplier=").Append(instrument.ContractMultiplier is decimal cm ? D(cm) : "null").Append('|');
    }

    private static void AppendPolicy(StringBuilder sb, RiskPolicy policy)
    {
        sb.Append("Policy.MaxRiskPerTradePercent=").Append(N(policy.MaxRiskPerTradePercent)).Append('|');
        sb.Append("Policy.MaxRiskPerTradeAmount=").Append(N(policy.MaxRiskPerTradeAmount)).Append('|');
        sb.Append("Policy.MaxDailyLossPercent=").Append(N(policy.MaxDailyLossPercent)).Append('|');
        sb.Append("Policy.MaxDailyLossAmount=").Append(N(policy.MaxDailyLossAmount)).Append('|');
        sb.Append("Policy.MaxDrawdownPercent=").Append(N(policy.MaxDrawdownPercent)).Append('|');
        sb.Append("Policy.MaxDrawdownAmount=").Append(N(policy.MaxDrawdownAmount)).Append('|');
        sb.Append("Policy.MaxOpenRiskPercent=").Append(N(policy.MaxOpenRiskPercent)).Append('|');
        sb.Append("Policy.MaxOpenRiskAmount=").Append(N(policy.MaxOpenRiskAmount)).Append('|');
        sb.Append("Policy.MinRiskReward=").Append(policy.MinRiskReward is double mrr ? D(mrr) : "null").Append('|');
        sb.Append("Policy.MaxPositionSize=").Append(policy.MaxPositionSize?.ToString(CultureInfo.InvariantCulture) ?? "null").Append('|');
        sb.Append("Policy.MinPositionSize=").Append(policy.MinPositionSize?.ToString(CultureInfo.InvariantCulture) ?? "null").Append('|');
    }

    private static string N(decimal? value) => value is decimal v ? D(v) : "null";

    // ── Evidence ─────────────────────────────────────────────────────────────────────────────────────

    private static void AppendAdf(StringBuilder sb, AdfResult? r)
    {
        sb.Append(" ADF=");
        if (r is null) { sb.Append("NULL"); return; }
        sb.Append("[Stat=").Append(D(r.Statistic))
          .Append(",PV=").Append(D(r.PValue))
          .Append(",Conf=").Append(D(r.Confidence))
          .Append(",CV1=").Append(D(r.CriticalValue1))
          .Append(",CV5=").Append(D(r.CriticalValue5))
          .Append(",CV10=").Append(D(r.CriticalValue10))
          .Append(",Stat?=").Append(r.IsStationary)
          .Append(",Lag=").Append(r.LagUsed)
          .Append(",N=").Append(r.SampleSize)
          .Append(",Valid=").Append(r.IsValid)
          .Append(']');
    }

    private static void AppendKpss(StringBuilder sb, KpssResult? r)
    {
        sb.Append(" KPSS=");
        if (r is null) { sb.Append("NULL"); return; }
        sb.Append("[Stat=").Append(D(r.Statistic))
          .Append(",PV=").Append(D(r.PValue))
          .Append(",Conf=").Append(D(r.Confidence))
          .Append(",CV1=").Append(D(r.CriticalValue1))
          .Append(",CV5=").Append(D(r.CriticalValue5))
          .Append(",CV10=").Append(D(r.CriticalValue10))
          .Append(",Stat?=").Append(r.IsStationary)
          .Append(",BW=").Append(r.Bandwidth)
          .Append(",N=").Append(r.SampleSize)
          .Append(",Valid=").Append(r.IsValid)
          .Append(']');
    }

    private static void AppendHurst(StringBuilder sb, HurstResult? r)
    {
        sb.Append(" Hurst=");
        if (r is null) { sb.Append("NULL"); return; }
        sb.Append("[VR=").Append(D(r.VarianceRatio))
          .Append(",H=").Append(D(r.HurstProxy))
          .Append(",Conf=").Append(D(r.Confidence))
          .Append(",Valid=").Append(r.IsValid)
          .Append(']');
    }

    private static void AppendHalfLife(StringBuilder sb, HalfLifeResult? r)
    {
        sb.Append(" HalfLife=");
        if (r is null) { sb.Append("NULL"); return; }
        sb.Append("[HL=").Append(D(r.HalfLife))
          .Append(",Lambda=").Append(D(r.Lambda))
          .Append(",Intercept=").Append(D(r.Intercept))
          .Append(",SE=").Append(D(r.StandardError))
          .Append(",R2=").Append(D(r.RSquared))
          .Append(",Conf=").Append(D(r.Confidence))
          .Append(",N=").Append(r.SampleSize)
          .Append(",Valid=").Append(r.IsValid)
          .Append(']');
    }

    private static void AppendVarianceRatio(StringBuilder sb, VarianceRatioResult? r)
    {
        sb.Append(" VarianceRatio=");
        if (r is null) { sb.Append("NULL"); return; }
        sb.Append("[VR=").Append(D(r.VarianceRatio))
          .Append(",Z=").Append(D(r.ZStatistic))
          .Append(",PV=").Append(D(r.PValue))
          .Append(",Conf=").Append(D(r.Confidence))
          .Append(",Lag=").Append(r.Lag)
          .Append(",N=").Append(r.SampleSize)
          .Append(",Valid=").Append(r.IsValid)
          .Append(']');
    }

    private static void AppendCusum(StringBuilder sb, CusumResult? r)
    {
        sb.Append(" Cusum=");
        if (r is null) { sb.Append("NULL"); return; }
        sb.Append("[Changed=").Append(r.ChangeDetected)
          .Append(",BreakIdx=").Append(r.EstimatedBreakIndex)
          .Append(",Pos=").Append(D(r.PositiveCusum))
          .Append(",Neg=").Append(D(r.NegativeCusum))
          .Append(",Thr=").Append(D(r.Threshold))
          .Append(",Conf=").Append(D(r.Confidence))
          .Append(",N=").Append(r.SampleSize)
          .Append(",Valid=").Append(r.IsValid)
          .Append(']');
    }

    private static void AppendVolatility(StringBuilder sb, VolatilityResult? r)
    {
        sb.Append(" Volatility=");
        if (r is null) { sb.Append("NULL"); return; }
        sb.Append("[Acf=").Append(D(r.AcfAbsReturns))
          .Append(",Clustering=").Append(r.IsClustering)
          .Append(",Conf=").Append(D(r.Confidence))
          .Append(",Valid=").Append(r.IsValid)
          .Append(']');
    }

    private static void AppendBaiPerron(StringBuilder sb, BaiPerronResult? r)
    {
        sb.Append(" BaiPerron=");
        if (r is null) { sb.Append("NULL"); return; }
        sb.Append("[Breaks=").Append(string.Join(',', r.Breakpoints))
          .Append(",Count=").Append(r.BreakCount)
          .Append(",Conf=").Append(D(r.Confidence))
          .Append(",RSS=").Append(D(r.GlobalRSS))
          .Append(",BIC=").Append(D(r.BicScore))
          .Append(",N=").Append(r.SampleSize)
          .Append(",Valid=").Append(r.IsValid)
          .Append(']');
    }

    private static void AppendDfa(StringBuilder sb, DfaResult? r)
    {
        sb.Append(" Dfa=");
        if (r is null) { sb.Append("NULL"); return; }
        sb.Append("[H=").Append(D(r.Hurst))
          .Append(",R2=").Append(D(r.RSquared))
          .Append(",Conf=").Append(D(r.Confidence))
          .Append(",Windows=").Append(r.WindowCount)
          .Append(",Valid=").Append(r.IsValid)
          .Append(']');
    }
}
