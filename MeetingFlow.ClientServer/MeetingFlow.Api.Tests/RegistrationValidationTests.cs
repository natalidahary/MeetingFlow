using Microsoft.AspNetCore.Mvc.Testing;

namespace MeetingFlow.Api.Tests;

public class RegistrationValidationTests
{
    // Checked an assumption before writing the real tests: is `Program`
    // even reachable from this test assembly for WebApplicationFactory<Program>?
    // Turns out yes — Program.cs uses top-level statements with no explicit
    // `public partial class Program {}`, but the type is public and this
    // compiles fine. So THIS specific blocker (the one most tutorials warn about)
    // does not apply here.
    [Fact]
    public void ProgramTypeIsPublic()
    {
        Assert.True(typeof(Program).IsPublic);
    }

    // What I wish I could write:
    //
    // [Fact]
    // public void PublishedMeeting_RegistrationSucceeds()
    // {
    //     var meeting = new { Status = "Published", RegistrationCount = 0, Capacity = 100 };
    //     var result = RegistrationValidator.Validate(meeting);
    //     Assert.Equal(ValidationResult.Accepted, result);
    // }
    //
    // [Fact]
    // public void DraftMeeting_RegistrationRejected()
    // {
    //     var meeting = new { Status = "Draft", RegistrationCount = 0, Capacity = 100 };
    //     var result = RegistrationValidator.Validate(meeting);
    //     Assert.Equal(ValidationResult.Rejected, result);
    // }
    //
    // [Fact]
    // public void FullMeeting_RegistrationRejected()
    // {
    //     var meeting = new { Status = "Published", RegistrationCount = 100, Capacity = 100 };
    //     var result = RegistrationValidator.Validate(meeting);
    //     Assert.Equal(ValidationResult.Rejected, result);
    // }
    //
    // But I can't write any of these for real, because:
    //
    // 1. The rules don't exist yet. RegistrationsEndpoints.cs (MapPost "/api/registrations")
    //    accepts every request unconditionally — no status check, no capacity check.
    //    There is nothing in production code to call that would ever return "Rejected".
    //    A test for the Draft/full-capacity cases would just fail against real code,
    //    not because of a test-infra problem but because the feature isn't built.
    //
    // 2. There's no extractable "validation" to call. The whole create-registration
    //    handler is one anonymous lambda passed straight to app.MapPost(...) inside
    //    MapRegistrationsEndpoints. It's never assigned to a variable or a named method,
    //    so a test can't `using MeetingFlow.Api.Endpoints;` and call it directly —
    //    there's no symbol to import. The only way to exercise it today is over real HTTP
    //    (TestServer / WebApplicationFactory), which means going through routing, model
    //    binding, and EF Core just to check an if/else.
    //
    // 3. Going the WebApplicationFactory route to invoke it over HTTP hits its own wall:
    //    Program.cs hardcodes `UseSqlite("Data Source=meetingflow_api.db")` — the real
    //    dev database file, not swappable via config or environment. Spinning up the
    //    factory as-is would EnsureCreated + reseed that real file. To isolate it you'd
    //    need `WithWebHostBuilder(b => b.ConfigureServices(...))` to remove the existing
    //    DbContext registration and replace it with an in-memory/sqlite-in-memory one —
    //    doable, but real extra plumbing, not something you get for free.
    //
    // 4. Even with an isolated DB, "full capacity" needs RegistrationCount computed from
    //    actual rows (Registrations.Count(r => r.MeetingId == id)) compared to
    //    Venue.Capacity — so the test would need to seed a Meeting, a Venue, and N
    //    Registration rows through EF Core just to test one comparison.
    //
    // Answering the specific questions:
    // - Can you call the endpoint logic without starting the web server?
    //   No — it's an inline lambda, not a named method. Nothing to call directly.
    // - Can you test the validation without hitting the real database?
    //   The validation doesn't exist yet. If it did, and it read Status/Capacity/
    //   RegistrationCount off entities, you'd need at least an isolated DbContext
    //   (in-memory provider) to build those entities — "no database at all" is only
    //   possible if the rule is extracted into a pure function taking plain values.
    // - Can you control what DateTimeOffset.UtcNow returns?
    //   No — RegisteredAt = DateTimeOffset.UtcNow is called directly in the handler,
    //   same issue as Part 1. No clock is injected.
    // - If you could extract the validation into a separate method/class, what would
    //   its signature look like?
    //   Something like:
    //     public enum ValidationResult { Accepted, Rejected }
    //     public static class RegistrationValidator
    //     {
    //         public static ValidationResult Validate(string meetingStatus, int registrationCount, int venueCapacity)
    //             => meetingStatus != "Published"      ? ValidationResult.Rejected
    //              : registrationCount >= venueCapacity ? ValidationResult.Rejected
    //              : ValidationResult.Accepted;
    //     }
    //   Pure function, three primitive inputs, one enum output — no DbContext, no HTTP,
    //   no clock. That's what would make the three scenarios above trivial to test.
}
