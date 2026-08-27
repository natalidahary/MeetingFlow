using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.RabbitMq;

namespace MeetingFlow.IntegrationTests;

/// <summary>
/// Answers one focused question: can RegistrationsManager's real EventPublisher and
/// NotificationsAccessor's real RegistrationEventConsumer actually talk to each other —
/// same exchange name, same queue/routing key binding, same RegistrationCreatedV1 JSON
/// shape?
///
/// Both classes are the unmodified production code. The only real infrastructure this
/// test brings up is a throwaway RabbitMQ broker (via Testcontainers) — no Postgres, no
/// Gateway, no other managers/engines are started; this is not the full MeetingFlow
/// system. NotificationsAccessor's own Postgres dependency is swapped for an EF Core
/// InMemory database purely so the consumer's DB write has somewhere to land — that
/// persistence behavior is DataAccessor/EF's concern, already covered separately, and
/// is not what this test is checking.
/// </summary>
public class RegistrationCreatedMessagingTests : IAsyncLifetime
{
    readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:3-management-alpine").Build();

    public Task InitializeAsync() => _rabbitMq.StartAsync();
    public Task DisposeAsync() => _rabbitMq.DisposeAsync().AsTask();

    [Fact]
    public async Task EventPublishedByRegistrationsManager_IsConsumedAndTurnedIntoANotification()
    {
        var services = new ServiceCollection();
        // The name must be captured once: AddDbContext's options callback runs again
        // for every new scope, so a Guid generated inline here would hand each scope
        // (including each poll iteration below) its own empty database.
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<global::NotificationsAccessor.Data.NotificationsDbContext>(o =>
            o.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new("RABBITMQ_URL", _rabbitMq.GetConnectionString())])
            .Build();
        var smtp = new global::NotificationsAccessor.Infrastructure.FakeSmtpGateway(
            NullLogger<global::NotificationsAccessor.Infrastructure.FakeSmtpGateway>.Instance);

        var consumer = new global::NotificationsAccessor.Messaging.RegistrationEventConsumer(
            scopeFactory, config, NullLogger<global::NotificationsAccessor.Messaging.RegistrationEventConsumer>.Instance, smtp);
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            // Give the consumer time to connect and declare/bind its queue before we
            // publish — a topic exchange drops messages with no bound queue yet.
            await Task.Delay(TimeSpan.FromSeconds(5));

            await using var publisher = await global::RegistrationsManager.Messaging.EventPublisher.CreateAsync(
                _rabbitMq.GetConnectionString());

            var registrationId = Guid.NewGuid();
            var attendeeId = Guid.NewGuid();
            await publisher.PublishAsync(
                "registration.created.v1",
                new MeetingFlow.IntegrationEvents.RegistrationCreatedV1(
                    EventId: Guid.NewGuid(),
                    RegistrationId: registrationId,
                    MeetingId: Guid.NewGuid(),
                    AttendeeId: attendeeId,
                    MeetingTitle: "Cloud Integration Day",
                    RecipientName: "Jane Doe",
                    RecipientEmail: "jane@example.com",
                    RegisteredAt: DateTimeOffset.UtcNow));

            var notification = await PollForNotificationAsync(scopeFactory, attendeeId, TimeSpan.FromSeconds(15));

            Assert.NotNull(notification);
            Assert.Equal("Email", notification!.Type);
            Assert.Contains("Cloud Integration Day", notification.Subject);
            Assert.Contains(registrationId.ToString(), notification.Body);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    static async Task<global::NotificationsAccessor.Models.Notification?> PollForNotificationAsync(
        IServiceScopeFactory scopeFactory, Guid attendeeId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<global::NotificationsAccessor.Data.NotificationsDbContext>();
            var found = await db.Notifications.SingleOrDefaultAsync(n => n.AttendeeId == attendeeId);
            if (found is not null) return found;
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
        return null;
    }
}
