# KosherCheck evals

Automated evaluation suite for the `/KosherCheck` AI assessment
(`../MeetingFlow.Monolith/Services/OpenAiKosherAssessmentService.cs`). For every case in
`cases/kosher-check.cases.json` it calls the real assessment service in-process with the
configured model, runs a deterministic (non-LLM) check, then asks a judge model to score the
result, and writes a Markdown report.

## Configure

Create `MeetingFlow.Monolith.Evals/appsettings.Local.json` (gitignored, not committed):

```json
{
  "AiChat": { "Model": "openai/gpt-oss-20b", "Endpoint": "https://api.groq.com/openai/v1", "ApiKey": "<key>" },
  "Judge":  { "Model": "openai/gpt-oss-120b", "Endpoint": "https://api.groq.com/openai/v1", "ApiKey": "<key>" }
}
```

The `Judge` section is optional -- omit it to grade with the same model and key as `AiChat`.

Setting names / environment variables (env vars override the file, `Section__Key` convention):

| Purpose                                   | Config key       | Environment variable |
|--------------------------------------------|------------------|-----------------------|
| Evaluated model API key                    | `AiChat:ApiKey`  | `AiChat__ApiKey`      |
| Evaluated model name                       | `AiChat:Model`   | `AiChat__Model`       |
| Evaluated model endpoint                   | `AiChat:Endpoint`| `AiChat__Endpoint`    |
| Judge model API key (optional)             | `Judge:ApiKey`   | `Judge__ApiKey`       |
| Judge model name (optional)                | `Judge:Model`    | `Judge__Model`        |
| Judge model endpoint (optional)            | `Judge:Endpoint` | `Judge__Endpoint`     |
| Repeats per case (optional, default 1)     | `Eval:Repeats`   | `Eval__Repeats`       |

`Eval:Repeats` reruns every case N times so you can see how consistent the evaluated model is on
a given input, not just whether one sample happened to pass. It multiplies API usage by N for both
the evaluated model and the judge, so raise it with care on a rate-limited free tier.

## Run

From the repository root:

```
dotnet run --project MeetingFlow.Monolith.Evals/MeetingFlow.Monolith.Evals.csproj
```

Add or edit scenarios in `cases/kosher-check.cases.json` without touching `Program.cs`. Pass a
different file path as the first argument to run an alternate case set.

## Report

Each run overwrites `MeetingFlow.Monolith.Evals/reports/eval-report.md` with: the run date, the
evaluated and judge models, the number of trials run per case, pass@k (passed at least one trial)
and pass^k (passed every trial) counts, the average judge score across all trials, a table with
every case's statuses seen across trials, pass count, average score, and a sample of judge reasons,
and a closing conclusion summarizing what the model does well, where it is inconsistent across
repeated trials, and where it fails outright. The process exits with a non-zero code if any case
did not pass every trial.
