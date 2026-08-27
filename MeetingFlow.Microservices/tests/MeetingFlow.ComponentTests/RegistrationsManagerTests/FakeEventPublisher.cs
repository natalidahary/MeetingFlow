using global::RegistrationsManager.Messaging;

namespace MeetingFlow.ComponentTests.RegistrationsManagerTests;

/// <summary>
/// Stands in for the real RabbitMQ-backed EventPublisher so tests never need
/// a broker. Records every publish so a test can assert on side effects
/// (e.g. "no event was published when the request was rejected").
/// </summary>
public class FakeEventPublisher : IEventPublisher
{
    public List<(string RoutingKey, object Message)> Published { get; } = [];

    public Task PublishAsync<T>(string routingKey, T message)
    {
        Published.Add((routingKey, message!));
        return Task.CompletedTask;
    }
}
