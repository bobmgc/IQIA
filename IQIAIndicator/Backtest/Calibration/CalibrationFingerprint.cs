using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using IQIAIndicator.Backtest.Cost;
using IQIAIndicator.Backtest.Execution;
using IQIAIndicator.Backtest.Measurement;
using IQIAIndicator.Backtest.Pnl;
using IQIAIndicator.Backtest.Risk;
using IQIAIndicator.Engine.Risk;

namespace IQIAIndicator.Backtest.Calibration;

/// <summary>
/// Sprint 15.25 (Lot 14.9, brief §20/§45). Deterministic, wall-clock-free canonicalization for the
/// calibration framework - same reuse strategy as every prior lot's fingerprint (Lot 14.1's
/// <see cref="BacktestFingerprint"/>, Lot 14.2's <see cref="HistoricalSeriesFingerprint"/>, Lot 14.7's
/// CostFingerprint, Lot 14.8's RiskResultFingerprint, ...): lives alongside those types and reuses their
/// ONE shared public primitive, <see cref="BacktestFingerprint.Sha256Hex"/>, without modifying that file
/// (brief §10/§20: "NE PAS créer un deuxième algorithme SHA inutilement").
///
/// <see cref="ComputeConfigurationFingerprint"/> depends on every input brief §20 names explicitly
/// (parameters, dataset, timeframe, window, measurement/execution/risk configuration, protocol version)
/// PLUS every other input that actually reaches <see cref="BacktestEngine.RunFullBacktestWithRisk"/> and
/// could change its output (Instrument, RiskPolicy, InitialCapital, PnL/Cost configuration, warmup) - a
/// superset of the brief's floor, not a subset, so brief §37/§38's mutation tests hold for every one of
/// those inputs, not only the ones named by §20.
/// </summary>
internal static class CalibrationFingerprint
{
    public static string ComputeParameterSetFingerprint(CalibrationParameterSet parameterSet)
    {
        var sb = new StringBuilder();
        AppendParameterSet(sb, parameterSet);
        return BacktestFingerprint.Sha256Hex(sb.ToString());
    }

    public static string ComputeConfigurationFingerprint(
        CalibrationParameterSet parameterSet,
        CalibrationDataset dataset,
        CalibrationWindowSet windows,
        CalibrationExperimentSetup setup)
    {
        var sb = new StringBuilder();
        sb.Append("ProtocolVersion=").Append(CalibrationProtocolVersion.Current).Append('\n');

        AppendParameterSet(sb, parameterSet);

        sb.Append("Dataset.Provider=").Append(dataset.Specification.Provider).Append('|');
        sb.Append("Dataset.Symbol=").Append(dataset.Specification.Symbol).Append('|');
        sb.Append("Dataset.Timeframe=").Append(dataset.Specification.Timeframe).Append('|');
        sb.Append("Dataset.Start=").Append(Dt(dataset.Specification.Start)).Append('|');
        sb.Append("Dataset.End=").Append(Dt(dataset.Specification.End)).Append('|');
        sb.Append("Dataset.Fingerprint=").Append(dataset.Fingerprint).Append('\n');

        AppendWindow(sb, "Train", windows.Train);
        AppendWindow(sb, "Validation", windows.Validation);
        AppendWindow(sb, "Oos", windows.Oos);

        sb.Append("WarmupContract.RequiredWarmupBars=").Append(setup.WarmupContract.RequiredWarmupBars).Append('\n');

        AppendInstrument(sb, setup.Instrument);
        AppendPolicy(sb, setup.Policy);
        sb.Append("InitialCapital=").Append(D(setup.InitialCapital)).Append('\n');

        AppendMeasurement(sb, setup.MeasurementConfiguration);
        AppendExecution(sb, setup.ExecutionConfiguration);
        AppendPnl(sb, setup.PnLConfiguration);
        AppendCost(sb, setup.CostConfiguration);
        AppendRisk(sb, setup.RiskConfiguration);

        return BacktestFingerprint.Sha256Hex(sb.ToString());
    }

    /// <summary>Brief §19: derived from the fingerprint, never a random Guid. Two experiments with
    /// identical configuration always compute the identical ExperimentId.</summary>
    public static string ComputeExperimentId(string configurationFingerprint) =>
        $"CAL-{configurationFingerprint[..16]}";

    public static string ComputeResultFingerprint(
        string experimentId,
        CalibrationWindow window,
        CalibrationDirectionFilter direction,
        CalibrationExperimentResultStatus status,
        int signalCount,
        int positionCount,
        decimal? grossPnL,
        decimal? finalEquity,
        decimal? maximumDrawdown,
        double? winRate,
        double? medianReturn,
        double? medianMfe,
        double? medianMae,
        IReadOnlyList<(double Threshold, double Rate)> hitRates)
    {
        var sb = new StringBuilder();
        sb.Append("ExperimentId=").Append(experimentId).Append('|');
        sb.Append("Window=").Append(window.Role).Append('[').Append(Dt(window.Start)).Append("..").Append(Dt(window.End)).Append(")|");
        sb.Append("Direction=").Append(direction).Append('|');
        sb.Append("Status=").Append(status).Append('|');
        sb.Append("SignalCount=").Append(signalCount).Append('|');
        sb.Append("PositionCount=").Append(positionCount).Append('|');
        sb.Append("GrossPnL=").Append(N(grossPnL)).Append('|');
        sb.Append("FinalEquity=").Append(N(finalEquity)).Append('|');
        sb.Append("MaximumDrawdown=").Append(N(maximumDrawdown)).Append('|');
        sb.Append("WinRate=").Append(N(winRate)).Append('|');
        sb.Append("MedianReturn=").Append(N(medianReturn)).Append('|');
        sb.Append("MedianMfe=").Append(N(medianMfe)).Append('|');
        sb.Append("MedianMae=").Append(N(medianMae)).Append('|');
        sb.Append("HitRates=");
        foreach ((double threshold, double rate) in hitRates.OrderBy(h => h.Threshold))
            sb.Append('[').Append(D(threshold)).Append('=').Append(D(rate)).Append(']');

        return BacktestFingerprint.Sha256Hex(sb.ToString());
    }

    // ── Section builders ─────────────────────────────────────────────────────────────────────────────

    private static void AppendParameterSet(StringBuilder sb, CalibrationParameterSet parameterSet)
    {
        sb.Append("Parameters:\n");
        // Sorted by Name (ordinal), independent of construction order, so two parameter sets built from
        // the same logical parameters in different orders always fingerprint identically (brief §30).
        foreach (CalibrationParameter parameter in parameterSet.Parameters.OrderBy(p => p.Name, System.StringComparer.Ordinal))
        {
            sb.Append("  ").Append(parameter.Name).Append(':').Append(parameter.Type)
              .Append('=').Append(ValueOf(parameter))
              .Append('[').Append(parameter.Unit).Append(']')
              .Append("(Min=").Append(N(parameter.Min)).Append(",Max=").Append(N(parameter.Max)).Append(",Step=").Append(N(parameter.Step)).Append(')')
              .Append('\n');
        }
    }

    private static string ValueOf(CalibrationParameter parameter) => parameter.Type switch
    {
        CalibrationParameterType.Integer => parameter.IntegerValue?.ToString(CultureInfo.InvariantCulture) ?? "null",
        CalibrationParameterType.Decimal => N(parameter.DecimalValue),
        CalibrationParameterType.Boolean => parameter.BooleanValue?.ToString() ?? "null",
        CalibrationParameterType.Enum => parameter.EnumValue ?? "null",
        _ => "null"
    };

    private static void AppendWindow(StringBuilder sb, string label, CalibrationWindow window)
    {
        sb.Append("Window.").Append(label).Append('=').Append(window.Role)
          .Append('[').Append(Dt(window.Start)).Append("..").Append(Dt(window.End)).Append(")\n");
    }

    private static void AppendInstrument(StringBuilder sb, InstrumentRiskSpecification instrument)
    {
        sb.Append("Instrument.Symbol=").Append(instrument.Symbol).Append('|');
        sb.Append("Instrument.TickSize=").Append(D(instrument.TickSize)).Append('|');
        sb.Append("Instrument.TickValue=").Append(D(instrument.TickValue)).Append('|');
        sb.Append("Instrument.PointValue=").Append(D(instrument.PointValue)).Append('|');
        sb.Append("Instrument.MinQuantity=").Append(instrument.MinQuantity).Append('|');
        sb.Append("Instrument.MaxQuantity=").Append(instrument.MaxQuantity).Append('|');
        sb.Append("Instrument.QuantityStep=").Append(instrument.QuantityStep).Append('|');
        sb.Append("Instrument.ContractMultiplier=").Append(instrument.ContractMultiplier is decimal cm ? D(cm) : "null").Append('\n');
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
        sb.Append("Policy.MaxPositionSize=").Append(policy.MaxPositionSize?.ToString(CultureInfo.InvariantCulture) ?? "null").Append('\n');
    }

    private static void AppendMeasurement(StringBuilder sb, MeasurementConfiguration configuration)
    {
        sb.Append("Measurement.HorizonBars=").Append(configuration.HorizonBars).Append('|');
        sb.Append("Measurement.HitThresholds=[").Append(string.Join(',', configuration.HitThresholds.Select(D))).Append("]\n");
    }

    private static void AppendExecution(StringBuilder sb, ExecutionConfiguration configuration)
    {
        sb.Append("Execution.HorizonBars=").Append(configuration.HorizonBars).Append('\n');
    }

    private static void AppendPnl(StringBuilder sb, PnLConfiguration configuration)
    {
        sb.Append("Pnl.Instrument.Symbol=").Append(configuration.Instrument.Symbol).Append('|');
        sb.Append("Pnl.Instrument.PriceUnitValue=").Append(D(configuration.Instrument.PriceUnitValue)).Append('|');
        sb.Append("Pnl.Instrument.Currency=").Append(configuration.Instrument.Currency).Append('|');
        sb.Append("Pnl.Quantity=").Append(configuration.Quantity).Append('|');
        sb.Append("Pnl.StartingCapital=").Append(N(configuration.StartingCapital)).Append('\n');
    }

    private static void AppendCost(StringBuilder sb, ExecutionCostConfiguration configuration)
    {
        sb.Append("Cost.Enabled=").Append(configuration.Enabled).Append('|');
        sb.Append("Cost.Slippage=").Append(D(configuration.Slippage.PriceUnits)).Append('|');
        sb.Append("Cost.Spread=").Append(D(configuration.Spread.PriceUnits)).Append('|');
        sb.Append("Cost.Commission.PerOrder=").Append(D(configuration.Commission.PerOrder)).Append('|');
        sb.Append("Cost.Commission.PerUnit=").Append(D(configuration.Commission.PerUnit)).Append('|');
        sb.Append("Cost.Fees.PerOrder=").Append(D(configuration.Fees.PerOrder)).Append('\n');
    }

    private static void AppendRisk(StringBuilder sb, BacktestRiskConfiguration configuration)
    {
        sb.Append("Risk.EnableRiskControls=").Append(configuration.EnableRiskControls).Append('|');
        sb.Append("Risk.RiskDistance=").Append(D(configuration.RiskDistance.PriceUnits)).Append('|');
        sb.Append("Risk.MaxExposure=").Append(N(configuration.MaxExposure)).Append('\n');
    }

    // ── Formatting primitives - same conventions as BacktestFingerprint (InvariantCulture, "O" dates) ──

    private static string D(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string D(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Dt(System.DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static string N(decimal? value) => value is decimal v ? D(v) : "null";

    private static string N(double? value) => value is double v ? D(v) : "null";
}
