using System.Net;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>An <see cref="IOptionsMonitor{T}"/> over a fixed value.</summary>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>
/// Records the requests it receives and answers each one from a queue of canned responses.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();

    public List<RecordedRequest> Requests { get; } = new();

    public StubHttpMessageHandler Respond(HttpStatusCode statusCode, string body = "")
    {
        _responders.Enqueue(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

        return this;
    }

    public StubHttpMessageHandler Respond(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responders.Enqueue(responder);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body, request.Headers.Authorization?.ToString()));

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException($"No canned response left for {request.Method} {request.RequestUri}.");
        }

        return _responders.Dequeue()(request);
    }

    internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, string? Authorization);
}

internal static class MaxioTestFactory
{
    public const string Subdomain = "acme";
    public const string ProductFamilyHandle = "demo-family";

    public static MaxioSettings Settings(Action<MaxioSettings>? configure = null)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-api-key",
            Subdomain = Subdomain,
            ProductFamilyHandle = ProductFamilyHandle,
            MaxRetryAttempts = 0,
            PlanCacheSeconds = 0
        };

        configure?.Invoke(settings);
        return settings;
    }

    public static MaxioApiClient Client(StubHttpMessageHandler handler, MaxioSettings? settings = null)
    {
        settings ??= Settings();

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = MaxioEnvironments.ResolveBaseAddress(settings)
        };

        return new MaxioApiClient(
            httpClient,
            new StaticOptionsMonitor<MaxioSettings>(settings),
            NullLoggerFactory.CreateLogger<MaxioApiClient>());
    }
}

internal static class NullLoggerFactory
{
    public static Microsoft.Extensions.Logging.ILogger<T> CreateLogger<T>() =>
        Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;
}
