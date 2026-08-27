using System.Net;
using System.Net.Http.Json;
using Npgsql;

namespace MeetingFlow.SystemTests;

/// <summary>
/// One black-box happy-path test against the fully deployed MeetingFlow stack
/// (docker compose up in MeetingFlow.Microservices): Gateway, RegistrationsManager,
/// SchedulingEngine, DataAccessor, RabbitMQ, NotificationsAccessor and Postgres are
/// all real, already-running processes reached over the network — nothing is faked,
/// in-process, or mocked. This does not repeat Part 2/3's business-rule or contract
/// scenarios; it only proves the deployed pieces are wired together correctly end to
/// end, entering through the one public boundary: the Gateway.
///
/// Environment: the test assumes `docker compose up` was already run (as in Part 0)
/// and does not manage container lifecycle itself — bringing up 8 freshly-built
/// service images per test run is too slow for routine use, so in a real pipeline
/// this tier runs as a separate stage after the environment is deployed. Endpoints
/// default to localhost but are overridable via GATEWAY_URL / NOTIFICATIONS_URL /
/// POSTGRES_CONN so the same test can run against a differently-hosted environment.
///
/// Scenario data: there is no public endpoint to create an Attendee, so the test
/// reuses a real seeded attendee — discovered only through public read endpoints
/// (never by reading seed code or the database directly) — and picks one confirmed,
/// via a public read, not to be already registered for the target meeting. The one
/// piece of state this test creates (its own Registration; the Notification is a
/// side effect of that) is deleted again in teardown via a direct SQL cleanup, so
/// the shared seed data and database are left exactly as they were found and the
/// test can be re-run indefinitely without accumulating garbage or ever colliding
/// with itself.
/// </summary>
public class RegistrationSystemTests : IAsyncLifetime
{
    static readonly string GatewayUrl =
        Environment.GetEnvironmentVariable("GATEWAY_URL") ?? "http://localhost:8080";
    static readonly string NotificationsUrl =
        Environment.GetEnvironmentVariable("NOTIFICATIONS_URL") ?? "http://localhost:5011";
    static readonly string PostgresConnectionString =
        Environment.GetEnvironmentVariable("POSTGRES_CONN")
        ?? "Host=localhost;Port=5432;Database=meetingflow;Username=meetingflow;Password=meetingflow";

    HttpClient _gateway = null!;
    HttpClient _notifications = null!;
    Guid? _createdRegistrationId;
    Guid? _createdNotificationId;

    public async Task InitializeAsync()
    {
        _gateway = new HttpClient { BaseAddress = new Uri(GatewayUrl) };
        _notifications = new HttpClient { BaseAddress = new Uri(NotificationsUrl) };
        await WaitUntilHealthyAsync(_gateway, TimeSpan.FromSeconds(60));
    }

    public async Task DisposeAsync()
    {
        // Leave the shared, persistent database exactly as this test found it.
        if (_createdRegistrationId is not null || _createdNotificationId is not null)
        {
            await using var conn = new NpgsqlConnection(PostgresConnectionString);
            await conn.OpenAsync();

            if (_createdRegistrationId is { } registrationId)
            {
                await using var cmd = new NpgsqlCommand(
                    """DELETE FROM registrations.registrations WHERE "Id" = @id""", conn);
                cmd.Parameters.AddWithValue("id", registrationId);
                await cmd.ExecuteNonQueryAsync();
            }

            if (_createdNotificationId is { } notificationId)
            {
                await using var cmd = new NpgsqlCommand(
                    """DELETE FROM notifications.notifications WHERE "Id" = @id""", conn);
                cmd.Parameters.AddWithValue("id", notificationId);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        _gateway.Dispose();
        _notifications.Dispose();
    }

    [Fact]
    public async Task RegistrationCreatedThroughGateway_CanBeReadBackAndProducesANotification()
    {
        var (meetingId, attendeeId) = await FindFreeMeetingAttendeePairAsync();

        // Act: create the registration through the one public entry point.
        var createResponse = await _gateway.PostAsJsonAsync("/registrations",
            new Gateway.Contracts.CreateRegistrationRequest(meetingId, attendeeId, "General"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<Gateway.Contracts.CreateRegistrationResult>();
        Assert.NotNull(created);
        _createdRegistrationId = created!.Registration.Id;

        // Assert 1: the saved registration can be read back through the public boundary.
        var registrations = await _gateway.GetFromJsonAsync<List<Gateway.Contracts.RegistrationDto>>(
            $"/registrations/by-meeting/{meetingId}");
        var readBack = registrations!.SingleOrDefault(r => r.Id == created.Registration.Id);
        Assert.NotNull(readBack);
        Assert.Equal(attendeeId, readBack!.AttendeeId);
        Assert.Equal("General", readBack.TicketType);

        // Assert 2: the async side effect eventually lands. NotificationsAccessor isn't
        // proxied through the Gateway (it's intentionally not customer-facing), so this
        // is the one place the test has to reach past the public boundary to observe
        // the flow completed — there is no other way to see it.
        var notification = await PollForNotificationAsync(attendeeId, created.Registration.Id, TimeSpan.FromSeconds(20));
        Assert.NotNull(notification);
        _createdNotificationId = notification!.Id;
        Assert.Contains(created.Registration.Id.ToString(), notification.Body);
    }

    async Task<(Guid MeetingId, Guid AttendeeId)> FindFreeMeetingAttendeePairAsync()
    {
        var meetings = await _gateway.GetFromJsonAsync<List<Gateway.Contracts.MeetingListItemDto>>("/meetings");
        var published = meetings!.Where(m => m.Status == "Published").ToList();
        Assert.True(published.Count >= 2, "Need at least two Published meetings in seed data to run this scenario.");

        var registrationsByMeeting = new Dictionary<Guid, List<Gateway.Contracts.RegistrationDto>>();
        foreach (var meeting in published)
        {
            registrationsByMeeting[meeting.Id] =
                (await _gateway.GetFromJsonAsync<List<Gateway.Contracts.RegistrationDto>>(
                    $"/registrations/by-meeting/{meeting.Id}"))!;
        }

        // Source real attendee ids from whoever is already registered somewhere, then
        // pick a (meeting, attendee) pair where that attendee isn't registered yet —
        // all discovered through public reads, never by inspecting seed code or the DB.
        foreach (var sourceMeeting in published)
        foreach (var candidate in registrationsByMeeting[sourceMeeting.Id])
        foreach (var targetMeeting in published.Where(m => m.Id != sourceMeeting.Id))
        {
            var alreadyThere = registrationsByMeeting[targetMeeting.Id]
                .Any(r => r.AttendeeId == candidate.AttendeeId);
            if (!alreadyThere)
            {
                return (targetMeeting.Id, candidate.AttendeeId);
            }
        }

        throw new InvalidOperationException(
            "Could not find a (meeting, attendee) pair free of an existing registration.");
    }

    async Task<NotificationsAccessor.Contracts.NotificationDto?> PollForNotificationAsync(
        Guid attendeeId, Guid registrationId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var found = await _notifications.GetFromJsonAsync<List<NotificationsAccessor.Contracts.NotificationDto>>(
                $"/notifications/by-attendee/{attendeeId}");
            var match = found?.FirstOrDefault(n => n.Body.Contains(registrationId.ToString()));
            if (match is not null) return match;
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        return null;
    }

    static async Task WaitUntilHealthyAsync(HttpClient client, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await client.GetAsync("/health");
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { /* Gateway not accepting connections yet. */ }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        throw new TimeoutException($"Gateway at {client.BaseAddress} did not become healthy within {timeout}.");
    }
}
