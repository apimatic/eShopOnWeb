using System.Net;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>Minimal <see cref="IOptionsMonitor{T}"/> over a fixed value.</summary>
public class TestOptionsMonitor<T> : IOptionsMonitor<T>
    where T : class
{
    public TestOptionsMonitor(T value)
    {
        CurrentValue = value;
    }

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>Records the requests it is given and replies with canned responses.</summary>
public class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public List<string?> Bodies { get; } = new();

    public RecordingHandler Enqueue(HttpStatusCode statusCode, string body = "")
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));

        return _responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
    }
}

public static class TestOptions
{
    public static MaxioOptions Valid(Action<MaxioOptions>? customize = null)
    {
        var options = new MaxioOptions
        {
            ApiKey = "api-key",
            Subdomain = "acme",
            ProductFamilyHandle = "eshop-subscribe",
            RetryBaseDelayMilliseconds = 1,
            PlanCacheSeconds = 0
        };

        customize?.Invoke(options);
        return options;
    }
}
