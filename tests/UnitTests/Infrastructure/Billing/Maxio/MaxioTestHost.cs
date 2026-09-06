using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// Builds the real Maxio service graph (options, typed client, retry handler, site cache) on top of
/// a scripted transport, so the tests exercise the shipped DI wiring rather than a hand-built
/// stand-in.
/// </summary>
public sealed class MaxioTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public MaxioTestHost(ScriptedHttpMessageHandler handler, IDictionary<string, string?>? settings = null)
    {
        Handler = handler;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>
            {
                ["Maxio:ApiKey"] = "test-key",
                ["Maxio:Subdomain"] = "test-site",
                ["Maxio:ProductFamilyHandle"] = "demo-plans",
                // Keep the tests fast: no backoff sleeps for the retry paths.
                ["Maxio:MaxRetryAttempts"] = "0"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaxioSubscriptionBilling(configuration);
        services.AddHttpClient<MaxioApiClient>().ConfigurePrimaryHttpMessageHandler(() => handler);

        _provider = services.BuildServiceProvider();
    }

    public ScriptedHttpMessageHandler Handler { get; }

    public ISubscriptionBillingService BillingService => _provider.GetRequiredService<ISubscriptionBillingService>();

    public void Dispose() => _provider.Dispose();
}

/// <summary>
/// Answers requests from a list of route rules and records everything that was sent, so a test can
/// assert on the exact calls the integration made.
/// </summary>
public sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Rule> _rules = new();

    public List<RecordedRequest> Requests { get; } = new();

    /// <summary>Registers a response for requests whose method and path contain <paramref name="pathFragment"/>.</summary>
    public ScriptedHttpMessageHandler On(HttpMethod method, string pathFragment, HttpStatusCode status, string body)
    {
        _rules.Add(new Rule(method, pathFragment, _ => new Response(status, body)));
        return this;
    }

    /// <summary>Registers a response that varies with how many times the route has already been hit.</summary>
    public ScriptedHttpMessageHandler OnSequence(HttpMethod method, string pathFragment, params (HttpStatusCode Status, string Body)[] responses)
    {
        var hits = 0;
        _rules.Add(new Rule(method, pathFragment, _ =>
        {
            var index = Math.Min(hits++, responses.Length - 1);
            return new Response(responses[index].Status, responses[index].Body);
        }));
        return this;
    }

    public int CountOf(HttpMethod method, string pathFragment) =>
        Requests.Count(r => r.Method == method && r.Path.Contains(pathFragment, StringComparison.OrdinalIgnoreCase));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.PathAndQuery;
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method, path, body);

        lock (Requests) Requests.Add(recorded);

        var rule = _rules.FirstOrDefault(r =>
            r.Method == request.Method && path.Contains(r.PathFragment, StringComparison.OrdinalIgnoreCase));

        if (rule is null)
        {
            throw new InvalidOperationException($"No scripted response for {request.Method} {path}.");
        }

        var response = rule.Respond(recorded);
        return new HttpResponseMessage(response.Status)
        {
            Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
            RequestMessage = request
        };
    }

    public sealed record RecordedRequest(HttpMethod Method, string Path, string? Body);

    private sealed record Response(HttpStatusCode Status, string Body);

    private sealed record Rule(HttpMethod Method, string PathFragment, Func<RecordedRequest, Response> Respond);
}
