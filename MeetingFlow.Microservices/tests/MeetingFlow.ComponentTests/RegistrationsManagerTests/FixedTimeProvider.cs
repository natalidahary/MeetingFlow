namespace MeetingFlow.ComponentTests.RegistrationsManagerTests;

/// <summary>Deterministic replacement for TimeProvider.System so pricing's day-count logic is reproducible.</summary>
public class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
