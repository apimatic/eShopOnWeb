using System.Net;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>Serves one fixed options instance, standing in for the configuration system.</summary>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T> where T : class
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>
/// Replays a scripted sequence of responses and records every request it was asked to send.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>
    /// Request bodies captured as they were sent - the client disposes each request (and with it
    /// its content) once the call completes, so they cannot be read afterwards.
    /// </summary>
    public List<string> RequestBodies { get; } = new();

    public StubHttpMessageHandler Enqueue(HttpStatusCode statusCode, string? json = null)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json ?? string.Empty, Encoding.UTF8, "application/json")
        });

        return this;
    }

    public StubHttpMessageHandler EnqueueThrow(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException($"No response scripted for {request.Method} {request.RequestUri}.");
        }

        return Task.FromResult(_responses.Dequeue()(request));
    }
}

internal static class MaxioTestOptions
{
    public static MaxioOptions Valid() => new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "test-family",
        PlanCacheDuration = TimeSpan.Zero,
        CustomerCacheDuration = TimeSpan.Zero,
        MaxRetryAttempts = 0
    };
}
