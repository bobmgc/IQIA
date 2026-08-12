using System.Collections.Generic;
using System.Linq;
using IQIAIndicator.Engine.ScientificModels.Abstractions;

namespace IQIAIndicator.Engine.ScientificFusion;

public sealed class ScientificAssessmentBuilder
{
    private static readonly IReadOnlyList<string> ExpectedModels = new[]
    {
        "KalmanFilterModel",
        "OrnsteinUhlenbeckModel",
        "DynamicZScoreModel",
        "VolatilityModel",
        "SPRTModel"
    };

    public double OverallConfidence { get; private set; }

    public List<string> EvidenceAgreement { get; } = new();

    public List<string> EvidenceConflict { get; } = new();

    public List<string> MissingEvidence { get; } = new();

    public List<string> ExecutedModels { get; } = new();

    public List<string> SuccessfulModels { get; } = new();

    public List<string> FailedModels { get; } = new();

    public List<ScientificModelResult> ScientificResults { get; } = new();

    public string Diagnostics { get; private set; } = string.Empty;

    public ScientificCoverageStatus CoverageStatus { get; private set; } = ScientificCoverageStatus.NoModelCoverage;

    public void Populate(IReadOnlyList<ScientificModelResult>? scientificResults)
    {
        OverallConfidence = 0.0;
        EvidenceAgreement.Clear();
        EvidenceConflict.Clear();
        MissingEvidence.Clear();
        ExecutedModels.Clear();
        SuccessfulModels.Clear();
        FailedModels.Clear();
        ScientificResults.Clear();
        Diagnostics = string.Empty;
        CoverageStatus = ScientificCoverageStatus.NoModelCoverage;

        if (scientificResults is null)
        {
            MissingEvidence.AddRange(ExpectedModels);
            Diagnostics = "ScientificResults list is null.";
            return;
        }

        // A non-null but empty list means the model registry returned no models at all for the
        // current methodology (ScientificModelRegistry.Resolve only wires models for
        // "MeanReversionMethodology" as of this sprint - see audit finding ARC-003) - nothing was
        // ever executed, which is a structurally different condition from models running and
        // individually failing or reporting insufficient data.
        CoverageStatus = scientificResults.Count == 0
            ? ScientificCoverageStatus.NoModelCoverage
            : ScientificCoverageStatus.ModelsExecuted;

        ScientificResults.AddRange(scientificResults);
        ExecutedModels.AddRange(scientificResults.Select(result => result.ModelName));

        var foundExpectedModels = new HashSet<string>(scientificResults.Select(result => result.ModelName), StringComparer.OrdinalIgnoreCase);

        foreach (var expectedModel in ExpectedModels)
        {
            if (foundExpectedModels.Contains(expectedModel))
            {
                continue;
            }

            MissingEvidence.Add(expectedModel);
        }

        foreach (var result in scientificResults)
        {
            if (result.Success)
            {
                SuccessfulModels.Add(result.ModelName);
            }
            else
            {
                FailedModels.Add(result.ModelName);
            }
        }

        EvidenceAgreement.AddRange(SuccessfulModels);
        if (SuccessfulModels.Any() && FailedModels.Any())
        {
            EvidenceConflict.AddRange(FailedModels);
        }

        OverallConfidence = ExpectedModels.Count == 0
            ? 0.0
            : SuccessfulModels.Count / (double)ExpectedModels.Count;

        Diagnostics = BuildDiagnostics();
    }

    public ScientificAssessment Build()
    {
        return new ScientificAssessment(
            OverallConfidence,
            EvidenceAgreement.AsReadOnly(),
            EvidenceConflict.AsReadOnly(),
            MissingEvidence.AsReadOnly(),
            ExecutedModels.AsReadOnly(),
            SuccessfulModels.AsReadOnly(),
            FailedModels.AsReadOnly(),
            ScientificResults.AsReadOnly(),
            Diagnostics,
            CoverageStatus);
    }

    private string BuildDiagnostics()
    {
        if (ScientificResults.Count == 0)
        {
            // Reached only from the non-null scientificResults branch (the null case returns early
            // with its own message above), so an empty ScientificResults here always means the model
            // registry produced zero models for the current methodology - not that registered models
            // ran and reported missing/insufficient data. See ScientificCoverageStatus for the
            // distinction this message must not blur.
            return "No scientific model coverage for the current methodology. No models are registered for this regime; none were executed.";
        }

        var diagnostics = new List<string>();

        if (MissingEvidence.Any())
        {
            diagnostics.Add($"Missing evidence: {string.Join(", ", MissingEvidence)}.");
        }

        if (SuccessfulModels.Any())
        {
            diagnostics.Add($"Executed and successful models: {string.Join(", ", SuccessfulModels)}.");
        }

        if (FailedModels.Any())
        {
            diagnostics.Add($"Executed but failed models: {string.Join(", ", FailedModels)}.");
        }

        if (EvidenceConflict.Any())
        {
            diagnostics.Add($"Evidence conflict detected for: {string.Join(", ", EvidenceConflict)}.");
        }
        else if (!MissingEvidence.Any())
        {
            diagnostics.Add("All expected evidence models executed without detected conflict.");
        }

        return string.Join(" ", diagnostics);
    }
}
