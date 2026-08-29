using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace IQIAIndicator.Tests.Research.StopLossCalibration.A1Calibration;

/// <summary>
/// Sprint 15.14 (brief §17, Phase F - run last). TEST is evaluated ONLY against the regions that came
/// out of TRAIN discovery, cross-referenced with whether VALIDATION already held (brief §16) - no new
/// region is ever created from TEST, and no threshold is adjusted after seeing it.
///
/// Classification (brief §17): CONFIRMED requires the region to have held at VALIDATION AND to hold at
/// TEST (same two diagnostics as A1ValidationConfirmation: CV&lt;=10%, mean drift&lt;=5pp). REJECTED if TEST's
/// CV blows past 10% or its mean collapses by more than 10 percentage points from TRAIN (a catastrophic
/// drift, double A1ValidationConfirmation's tolerance, used only to distinguish "meaningfully worse" from
/// "collapsed"). Everything in between is PARTIALLY CONFIRMED.
/// </summary>
public enum RegionVerdict { CONFIRMED, PARTIALLY_CONFIRMED, REJECTED }

public sealed record RegionTestResult(
    string Scope,
    double StartK,
    double EndK,
    double TrainMean,
    double ValidationMean,
    bool ValidationHeld,
    double TestMean,
    double TestCv,
    double TestDrift,
    bool TestCvHolds,
    bool TestMeanHolds,
    RegionVerdict Verdict);

public static class A1TestConfirmation
{
    private const double CatastrophicDriftThreshold = 0.10;

    public static IReadOnlyList<RegionTestResult> Confirm(
        IReadOnlyList<CandidateAggregateResult> allRows,
        IReadOnlyList<RegionCandidate> trainRegions,
        IReadOnlyList<RegionSplitCheck> validationChecks)
    {
        var testChecks = A1ValidationConfirmation.Check(allRows, trainRegions, "TEST");
        var results = new List<RegionTestResult>();

        for (int i = 0; i < trainRegions.Count; i++)
        {
            RegionCandidate region = trainRegions[i];
            RegionSplitCheck validation = validationChecks[i];
            RegionSplitCheck test = testChecks[i];

            bool validationHeld = validation.CvHolds && validation.MeanHolds;
            bool testHolds = test.CvHolds && test.MeanHolds;
            bool catastrophic = !double.IsNaN(test.MeanDrift) && System.Math.Abs(test.MeanDrift) > CatastrophicDriftThreshold;

            RegionVerdict verdict;
            if (validationHeld && testHolds)
            {
                verdict = RegionVerdict.CONFIRMED;
            }
            else if (!test.CvHolds && catastrophic)
            {
                verdict = RegionVerdict.REJECTED;
            }
            else
            {
                verdict = RegionVerdict.PARTIALLY_CONFIRMED;
            }

            results.Add(new RegionTestResult(
                region.Scope, region.StartK, region.EndK,
                region.TrainMeanReversionRate, validation.SplitMean, validationHeld,
                test.SplitMean, test.SplitCv, test.MeanDrift, test.CvHolds, test.MeanHolds,
                verdict));
        }

        return results;
    }

    public static string Write(string outputDirectory, IReadOnlyList<RegionTestResult> results)
    {
        string path = Path.Combine(outputDirectory, "A1_k_test_confirmation.csv");
        using var writer = new StreamWriter(path, false);
        writer.WriteLine("Scope,StartK,EndK,TrainMean,ValidationMean,ValidationHeld,TestMean,TestCv,TestDrift,TestCvHolds,TestMeanHolds,Verdict");
        foreach (RegionTestResult r in results.OrderBy(r => r.Scope).ThenBy(r => r.StartK))
        {
            writer.WriteLine(string.Join(",",
                r.Scope, Fmt(r.StartK), Fmt(r.EndK), Fmt(r.TrainMean), Fmt(r.ValidationMean), r.ValidationHeld,
                Fmt(r.TestMean), Fmt(r.TestCv), Fmt(r.TestDrift), r.TestCvHolds, r.TestMeanHolds, r.Verdict));
        }

        return path;
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "" : v.ToString("G6", CultureInfo.InvariantCulture);
}
