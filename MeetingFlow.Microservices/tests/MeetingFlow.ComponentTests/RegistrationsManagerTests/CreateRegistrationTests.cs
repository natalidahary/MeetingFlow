using System.Net;
using System.Net.Http.Json;
using DataAccessor.Contracts;
using MeetingFlow.IntegrationEvents;
using RegistrationsManager.Contracts;
using SchedulingEngine.Contracts;

namespace MeetingFlow.ComponentTests.RegistrationsManagerTests;

/// <summary>
/// Component tests for RegistrationsManager's responsibility: deciding whether a
/// registration request should be accepted, computing its price, persisting it and
/// announcing it — without ever validating DataAccessor's, SchedulingEngine's or
/// RabbitMQ's own behavior (those are each other services' responsibilities).
///
/// The SUT is the whole RegistrationsManager process, exercised in-process through
/// its real HTTP pipeline. DataAccessor and SchedulingEngine are replaced by scripted
/// HTTP stubs; RabbitMQ is replaced by an in-memory fake; the clock is fixed.
/// </summary>
public class CreateRegistrationTests
{
    static readonly Guid MeetingId = Guid.Parse("b2000000-0000-0000-0000-000000000001");
    static readonly Guid AttendeeId = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidRequest_ForPublishedMeetingWithCapacity_IsAcceptedWithCorrectPriceAndAnnouncedOnce()
    {
        using var factory = new RegistrationsManagerFactory { UtcNow = Now };

        var meeting = new RegistrationMeetingContextDto(
            MeetingId, "Cloud Integration Day", "Published", Now.AddDays(30), VenueCapacity: 100);
        var attendee = new AttendeeContactDto(AttendeeId, "Jane Doe", "jane@example.com");
        var savedRegistrationId = Guid.NewGuid();

        factory.DataAccessor
            .When(HttpMethod.Get, $"/data/meetings/{MeetingId}/registration-context", HttpStatusCode.OK, meeting)
            .When(HttpMethod.Get, $"/data/registrations/by-meeting/{MeetingId}", HttpStatusCode.OK, Array.Empty<DataAccessor.Contracts.RegistrationDto>())
            .When(HttpMethod.Get, $"/data/attendees/{AttendeeId}/contact", HttpStatusCode.OK, attendee)
            .When(HttpMethod.Post, "/data/registrations", HttpStatusCode.Created,
                new DataAccessor.Contracts.RegistrationDto(savedRegistrationId, MeetingId, AttendeeId, Now, "General", "Pending", Attendee: null));

        factory.Scheduling.When(HttpMethod.Post, "/scheduling/check-capacity", HttpStatusCode.OK,
            new CheckCapacityResult(HasCapacity: true, AvailablePlaces: 100));

        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/registrations",
            new CreateRegistrationRequest(MeetingId, AttendeeId, "General"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CreateRegistrationResult>();
        Assert.NotNull(result);
        // General ticket base price is 199; the meeting is 30 days out, so neither the
        // <7-day surcharge nor the >60-day discount in InlineTicketPricing applies.
        Assert.Equal(199.00m, result!.CalculatedPrice);
        Assert.Equal(savedRegistrationId, result.Registration.Id);

        var published = Assert.Single(factory.EventPublisher.Published);
        Assert.Equal("registration.created.v1", published.RoutingKey);
        var evt = Assert.IsType<RegistrationCreatedV1>(published.Message);
        Assert.Equal(savedRegistrationId, evt.RegistrationId);
        Assert.Equal(AttendeeId, evt.AttendeeId);
        Assert.Equal("jane@example.com", evt.RecipientEmail);
    }

    [Fact]
    public async Task Request_ForMeetingAtCapacity_IsRejectedAndHasNoSideEffects()
    {
        using var factory = new RegistrationsManagerFactory { UtcNow = Now };

        var meeting = new RegistrationMeetingContextDto(
            MeetingId, "Cloud Integration Day", "Published", Now.AddDays(30), VenueCapacity: 1);
        var attendee = new AttendeeContactDto(AttendeeId, "Jane Doe", "jane@example.com");

        factory.DataAccessor
            .When(HttpMethod.Get, $"/data/meetings/{MeetingId}/registration-context", HttpStatusCode.OK, meeting)
            .When(HttpMethod.Get, $"/data/registrations/by-meeting/{MeetingId}", HttpStatusCode.OK, Array.Empty<DataAccessor.Contracts.RegistrationDto>())
            .When(HttpMethod.Get, $"/data/attendees/{AttendeeId}/contact", HttpStatusCode.OK, attendee);
        // Deliberately no stub for POST /data/registrations: if the handler tried to
        // persist anyway, the stub throws and fails the test with a clear message.

        factory.Scheduling.When(HttpMethod.Post, "/scheduling/check-capacity", HttpStatusCode.OK,
            new CheckCapacityResult(HasCapacity: false, AvailablePlaces: 0));

        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/registrations",
            new CreateRegistrationRequest(MeetingId, AttendeeId, "General"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("Meeting is at capacity", body?["error"]);
        Assert.Empty(factory.EventPublisher.Published);
    }
}
