#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>
/// A recorded HTTP request: what actually left the process, kept after the request message itself
/// has been disposed.
/// </summary>
public sealed class RecordedRequest
{
    public required HttpMethod Method { get; init; }
    public required string Host { get; init; }
    public required string Path { get; init; }
    public required string Query { get; init; }
    public required string Body { get; init; }
}

/// <summary>
/// Fakes Maxio at the <see cref="HttpMessageHandler"/> seam - the only seam the SDK offers - so
/// no test touches the network. Retries and refused re-sends both show up in
/// <see cref="Requests"/>, which is what makes "exactly one write reached the provider"
/// assertable.
/// </summary>
public sealed class MaxioStubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;

    public MaxioStubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) =>
        _responder = responder;

    public List<RecordedRequest> Requests { get; } = new();

    public int CountOf(HttpMethod method, string pathFragment) =>
        Requests.Count(request => request.Method == method && request.Path.Contains(pathFragment, StringComparison.OrdinalIgnoreCase));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest
        {
            Method = request.Method,
            Host = request.RequestUri?.Host ?? string.Empty,
            Path = request.RequestUri?.AbsolutePath ?? string.Empty,
            Query = request.RequestUri?.Query ?? string.Empty,
            Body = body
        });

        return _responder(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
}

/// <summary>
/// Builds the integration exactly as the host does - through
/// <see cref="MaxioBillingServiceCollectionExtensions.AddMaxioSubscriptionBilling"/> - with only
/// the primary HTTP handler swapped for a stub. That keeps the write-once handler, the timeouts
/// and the base-URL resolution under test instead of being bypassed.
/// </summary>
public static class MaxioTestHost
{
    public static ServiceProvider Build(MaxioStubHandler handler, IDictionary<string, string?>? settings = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Maxio:ApiKey"] = "test-api-key",
            ["Maxio:Subdomain"] = "test-site",
            ["Maxio:ProductFamilyHandle"] = "test-family",

            // Caching is off by default in tests so each test sees the calls it makes.
            ["Maxio:PlanCacheSeconds"] = "0"
        };

        if (settings is not null)
        {
            foreach (var pair in settings)
            {
                values[pair.Key] = pair.Value;
            }
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaxioSubscriptionBilling(configuration);
        services.AddHttpClient(MaxioBillingServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        return services.BuildServiceProvider();
    }

    public static ISubscriptionService Service(this ServiceProvider provider) =>
        provider.GetRequiredService<ISubscriptionService>();
}
