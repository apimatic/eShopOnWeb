using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing.Maxio;

/// <summary>
/// Builds the real billing service over a stubbed transport.
/// <para>
/// The service is resolved from the same <see cref="IServiceCollection"/> registration production uses, so
/// the tests exercise the options binding, the base-URL derivation and the write guard as they are actually
/// wired - only the socket is replaced. The seam is the <see cref="HttpClient"/> the SDK client is
/// constructed from, which is the only seam it offers.
/// </para>
/// </summary>
public sealed class MaxioTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    private MaxioTestHost(ServiceProvider provider, MaxioStubHandler handler)
    {
        _provider = provider;
        Handler = handler;
        Service = provider.GetRequiredService<ISubscriptionBillingService>();
    }

    public MaxioStubHandler Handler { get; }

    public ISubscriptionBillingService Service { get; }

    public static MaxioTestHost Create(MaxioStubHandler handler, IDictionary<string, string?>? settings = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-api-key",
            ["Maxio:Subdomain"] = "test-site",
            ["Maxio:ProductFamilyHandle"] = "eshop-subscribe",
            ["Maxio:DefaultPlanHandle"] = "eshop-pro",
            // Keep the retry pipeline at its floor so transport-failure tests do not sit through backoff.
            ["Maxio:MaxRetries"] = "1",
            ["Maxio:AttemptTimeoutSeconds"] = "5",
            ["Maxio:CallBudgetSeconds"] = "10"
        };

        if (settings is not null)
        {
            foreach (var setting in settings)
            {
                values[setting.Key] = setting.Value;
            }
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaxioSubscriptionBilling(configuration);
        services.AddHttpClient(MaxioBillingServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return new MaxioTestHost(services.BuildServiceProvider(), handler);
    }

    public void Dispose() => _provider.Dispose();
}

/// <summary>
/// Routes stubbed responses by HTTP method and path, and records every request that reaches it - which is
/// what makes "exactly one write left the process" assertable.
/// </summary>
public sealed class MaxioStubHandler : HttpMessageHandler
{
    private readonly List<Func<HttpRequestMessage, HttpResponseMessage?>> _routes = new();
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    public IReadOnlyCollection<RecordedRequest> Requests => _requests.ToArray();

    public Uri? LastRequestUri { get; private set; }

    public MaxioStubHandler Route(HttpMethod method, string pathContains, HttpStatusCode status, string json)
    {
        _routes.Add(request =>
            request.Method == method && (request.RequestUri?.AbsolutePath.Contains(pathContains) ?? false)
                ? Json(status, json)
                : null);
        return this;
    }

    /// <summary>Routes a response whose body depends on what has happened so far, the way a real site does.</summary>
    public MaxioStubHandler RouteFunc(HttpMethod method, string pathContains,
        Func<HttpRequestMessage, string> body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes.Add(request =>
            request.Method == method && (request.RequestUri?.AbsolutePath.Contains(pathContains) ?? false)
                ? Json(status, body(request))
                : null);
        return this;
    }

    /// <summary>Fails the matching request at the transport level, the way a dropped connection would.</summary>
    public MaxioStubHandler Fail(HttpMethod method, string pathContains)
    {
        _routes.Add(request =>
            request.Method == method && (request.RequestUri?.AbsolutePath.Contains(pathContains) ?? false)
                ? throw new HttpRequestException("connection reset")
                : (HttpResponseMessage?)null);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri!, body));
        LastRequestUri = request.RequestUri;

        foreach (var route in _routes)
        {
            var response = route(request);
            if (response is not null)
            {
                return response;
            }
        }

        return Json(HttpStatusCode.NotFound, "{}");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body);
}
