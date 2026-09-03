using System.ComponentModel;

namespace MeetingFlow.Monolith.Evals;

public sealed class EvalCaseDefinition
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public required string Dish { get; init; }
    public List<string>? ExpectedStatuses { get; init; }
    public string? Focus { get; init; }
}

public sealed record DeterministicCheckResult(bool Pass, string Details);

public sealed class JudgeVerdict
{
    [Description("Must exactly equal the caseId given in the input.")]
    public required string CaseId { get; init; }

    [Description("A quality score from 1 (bad) to 5 (excellent), out of MaxScore.")]
    public required int Score { get; init; }

    [Description("Always 5, the maximum possible score.")]
    public required int MaxScore { get; init; }

    [Description("True only when Score is 4 or 5 out of MaxScore.")]
    public required bool Passed { get; init; }

    [Description("Two to four short bullet-point reasons supporting the score, each grounded in the response being graded.")]
    public required List<string> Reasons { get; init; }
}

public sealed record TrialResult(
    string? Status,
    string? Explanation,
    DeterministicCheckResult Deterministic,
    JudgeVerdict? Judge,
    string? FailureReason)
{
    public bool OverallPass => Deterministic.Pass && (Judge?.Passed ?? false);
}

public sealed record EvalResult(string CaseId, string Category, string Dish, List<TrialResult> Trials)
{
    public int TrialCount => Trials.Count;
    public int PassCount => Trials.Count(t => t.OverallPass);

    /// <summary>pass@k: at least one of the repeated trials passed.</summary>
    public bool PassAtLeastOnce => PassCount > 0;

    /// <summary>pass^k: every one of the repeated trials passed.</summary>
    public bool PassEveryTrial => TrialCount > 0 && PassCount == TrialCount;

    public double? AverageScore => Trials.Any(t => t.Judge is not null)
        ? Trials.Where(t => t.Judge is not null).Average(t => (double)t.Judge!.Score)
        : null;

    public IReadOnlyList<(string Status, int Count)> StatusCounts => Trials
        .GroupBy(t => t.Status ?? "n/a")
        .OrderByDescending(group => group.Count())
        .Select(group => (group.Key, group.Count()))
        .ToList();
}
