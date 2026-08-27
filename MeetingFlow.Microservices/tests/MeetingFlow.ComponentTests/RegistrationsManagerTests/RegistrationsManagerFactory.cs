using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeetingFlow.ComponentTests.RegistrationsManagerTests;

/// <summary>
/// Runs the real RegistrationsManager ASP.NET Core pipeline in-process (routing, model
/// binding, the /registrations handler, pricing, mapping) while replacing every
/// out-of-process dependency with a controllable double:
///   - DataAccessor and SchedulingEngine calls never leave the process (stub transport).
///   - IEventPublisher never opens a real RabbitMQ connection (in-memory fake).
///   - TimeProvider is fixed so pricing's day-count math is deterministic.
/// </summary>
public class RegistrationsManagerFactory : WebApplicationFactory<Program>
{
    public StubHttpMessageHandler DataAccessor { get; } = new();
    public StubHttpMessageHandler Scheduling { get; } = new();
    public FakeEventPublisher EventPublisher { get; } = new();
    public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<global::RegistrationsManager.Messaging.IEventPublisher>();
            services.AddSingleton<global::RegistrationsManager.Messaging.IEventPublisher>(EventPublisher);

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(UtcNow));

            services.AddHttpClient<global::RegistrationsManager.Clients.DataAccessorClient>()
                .ConfigurePrimaryHttpMessageHandler(() => DataAccessor);
            services.AddHttpClient<global::RegistrationsManager.Clients.SchedulingEngineClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Scheduling);
        });
    }
}
