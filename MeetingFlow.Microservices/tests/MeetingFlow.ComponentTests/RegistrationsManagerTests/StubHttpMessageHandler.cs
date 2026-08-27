using System.Net;
using System.Net.Http.Json;

namespace MeetingFlow.ComponentTests.RegistrationsManagerTests;

/// <summary>
/// Replaces the real network transport for a downstream typed HttpClient.
/// Requests are matched by HTTP method + path so a test can script exactly
/// the responses DataAccessor/SchedulingEngine would have returned, without
/// either service actually running.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string Path, Func<HttpResponseMessage> Respond)> _routes = [];
    public List<(HttpMethod Method, string Path)> ReceivedRequests { get; } = [];

    public StubHttpMessageHandler When(HttpMethod method, string path, HttpStatusCode status, object? body = null)
    {
        _routes.Add((method, path, () => body is null
            ? new HttpResponseMessage(status)
            : new HttpResponseMessage(status) { Content = JsonContent.Create(body) }));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        ReceivedRequests.Add((request.Method, path));

        var route = _routes.FirstOrDefault(r => r.Method == request.Method && r.Path == path);
        if (route.Respond is null)
        {
            throw new InvalidOperationException(
                $"No stub configured for {request.Method} {path}");
        }

        return Task.FromResult(route.Respond());
    }
}
