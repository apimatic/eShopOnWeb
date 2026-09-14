using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>
/// Builds a <see cref="MaxioSubscriptionBillingService"/> over a stubbed transport.
/// </summary>
/// <remarks>
/// The SDK client takes an <see cref="HttpClient"/>, which is the seam: nothing here reaches the network and
/// nothing depends on SDK internals. The real <see cref="MaxioHttpDiagnosticsHandler"/> is deliberately kept
/// in the pipeline, because the write-once guarantee it provides is one of the behaviours under test.
/// </remarks>
internal static class MaxioTestHost
{
    public static MaxioSettings DefaultSettings() => new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = "demo-subscriptions"
    };

    public static MaxioSubscriptionBillingService CreateService(StubMaxioTransport transport, MaxioSettings? settings = null)
    {
        settings ??= DefaultSettings();

        var httpClient = new HttpClient(new MaxioHttpDiagnosticsHandler(
            Options.Create(settings),
            NullLogger<MaxioHttpDiagnosticsHandler>.Instance)
        {
            InnerHandler = transport
        });

        var client = new MaxioAdvancedBillingClient(
            httpClient,
            MaxioBillingServiceCollectionExtensions.BuildOptions(settings));

        return CreateService(new MaxioClientAccessor(client), settings);
    }

    public static MaxioSubscriptionBillingService CreateService(MaxioClientAccessor accessor, MaxioSettings? settings = null) =>
        new(accessor,
            Options.Create(settings ?? DefaultSettings()),
            NullLogger<MaxioSubscriptionBillingService>.Instance);
}

/// <summary>
/// A stub transport that answers by route and records every request that actually reached it.
/// </summary>
/// <remarks>
/// It records requests rather than calls, because a retry appends: the count is what proves whether a write
/// was re-sent.
/// </remarks>
internal sealed class StubMaxioTransport : HttpMessageHandler
{
    private readonly List<Route> _routes = new();
    private readonly List<RecordedRequest> _requests = new();

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToList();
            }
        }
    }

    public int CountOf(HttpMethod method, string pathContains) =>
        Requests.Count(request =>
            request.Method == method
            && request.Path.Contains(pathContains, StringComparison.OrdinalIgnoreCase));

    public StubMaxioTransport Respond(HttpMethod method, string pathContains, HttpStatusCode status, string json)
    {
        _routes.Add(new Route(
            request => request.Method == method && request.Path.Contains(pathContains, StringComparison.OrdinalIgnoreCase),
            _ => Json(status, json)));

        return this;
    }

    /// <summary>Answers a route from mutable state, so a test can model "the write landed".</summary>
    public StubMaxioTransport Respond(HttpMethod method, string pathContains, Func<(HttpStatusCode Status, string Json)> responder)
    {
        _routes.Add(new Route(
            request => request.Method == method && request.Path.Contains(pathContains, StringComparison.OrdinalIgnoreCase),
            _ =>
            {
                var (status, json) = responder();
                return Json(status, json);
            }));

        return this;
    }

    /// <summary>Answers a route by throwing, which is how a dropped connection reaches the SDK.</summary>
    public StubMaxioTransport Fail(HttpMethod method, string pathContains, Exception exception)
    {
        _routes.Add(new Route(
            request => request.Method == method && request.Path.Contains(pathContains, StringComparison.OrdinalIgnoreCase),
            _ => throw exception));

        return this;
    }

    /// <summary>
    /// Answers any request no earlier route claimed. Used for the reconciliation read, so that the test
    /// asserts the behaviour - a read after a failed write - rather than a URL this code never builds itself.
    /// </summary>
    public StubMaxioTransport RespondToAnythingElse(HttpStatusCode status, string json)
    {
        _routes.Add(new Route(_ => true, _ => Json(status, json)));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();

        var recorded = new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, body);

        lock (_requests)
        {
            _requests.Add(recorded);
        }

        foreach (var route in _routes)
        {
            if (route.Matches(recorded))
            {
                return Task.FromResult(route.Respond(recorded));
            }
        }

        return Task.FromResult(Json(HttpStatusCode.NotFound, """{"errors":["not stubbed"]}"""));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    internal sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string Body);

    private sealed record Route(Func<RecordedRequest, bool> Matches, Func<RecordedRequest, HttpResponseMessage> Respond);
}
