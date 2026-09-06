using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Builds a <see cref="MaxioBillingService"/> over a fake transport.
/// </summary>
/// <remarks>
/// The seam is the <see cref="HttpClient"/> the SDK client is constructed with, so these tests exercise
/// the real SDK — its serialization, its error types and its retry pipeline — with only the network
/// replaced. The write guard sits in the same position as in production, so the write-once behaviour
/// under a transport fault is genuinely under test rather than simulated.
/// </remarks>
internal static class MaxioTestHost
{
    public const string FamilyHandle = "test-family";
    public const int FamilyId = 777;
    public const int CustomerId = 42;

    public static (MaxioBillingService Service, RoutingHandler Transport) Create(
        Action<RoutingHandler>? configure = null,
        Action<MaxioSettings>? configureSettings = null)
    {
        var transport = new RoutingHandler();
        configure?.Invoke(transport);

        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FamilyHandle,
            PaymentCollectionMethod = "remittance",
            // Keep the tests fast: the SDK's floor is one retry, which is what the transport-fault test
            // needs in order to prove the guard blocks the resend.
            MaxRetries = 1,
            RetryTimeoutSeconds = 5,
            CallBudgetSeconds = 10
        };

        configureSettings?.Invoke(settings);

        var httpClient = new HttpClient(new MaxioWriteGuardHandler { InnerHandler = transport });

        var client = new MaxioAdvancedBillingClient(httpClient, new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey!, Password = "x" }
        });

        var service = new MaxioBillingService(
            client,
            Options.Create(settings),
            NullLogger<MaxioBillingService>.Instance);

        return (service, transport);
    }

    /// <summary>The standard catalog replies, so each test only states what it is actually about.</summary>
    public static RoutingHandler WithCatalog(this RoutingHandler transport)
    {
        transport.OnGet("/product_families.json",
            $$$"""[{"product_family": {"id": {{{FamilyId}}}, "handle": "{{{FamilyHandle}}}", "name": "Test Family"}}]""");

        transport.OnGet($"/product_families/{FamilyId}/products.json",
            """
            [
              {"product": {"id": 1, "handle": "pro-plan", "name": "Pro Plan", "price_in_cents": 29900,
                           "interval": 1, "interval_unit": "month", "require_credit_card": false, "taxable": false}},
              {"product": {"id": 2, "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900,
                           "interval": 12, "interval_unit": "month", "require_credit_card": false, "taxable": false}}
            ]
            """);

        return transport;
    }

    public static string SubscriptionJson(int id, string planHandle, string state) =>
        $$$"""
        {"subscription": {"id": {{{id}}}, "state": "{{{state}}}",
          "product": {"handle": "{{{planHandle}}}", "name": "Pro Plan", "price_in_cents": 29900,
                      "interval": 1, "interval_unit": "month"},
          "product_price_in_cents": 29900,
          "payment_collection_method": "remittance",
          "current_period_started_at": "2026-01-01T00:00:00Z",
          "current_period_ends_at": "2026-02-01T00:00:00Z",
          "next_assessment_at": "2026-02-01T00:00:00Z",
          "created_at": "2026-01-01T00:00:00Z",
          "activated_at": "2026-01-01T00:00:01Z"}}
        """;

    public static string CustomerJson(int id, string reference) =>
        $$$"""
        {"customer": {"id": {{{id}}}, "reference": "{{{reference}}}", "email": "someone@example.com",
          "first_name": "Someone", "last_name": "Example"}}
        """;
}

/// <summary>
/// Answers by method and path, and records every request that reached it. Retries append, so
/// <see cref="Requests"/> is what a test counts to prove a write was — or was not — resent.
/// </summary>
internal sealed class RoutingHandler : HttpMessageHandler
{
    private readonly List<Route> _routes = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public List<string> Bodies { get; } = new();

    public int CountOf(HttpMethod method, string pathFragment) =>
        Requests.Count(r => r.Method == method && r.RequestUri!.AbsolutePath.Contains(pathFragment, StringComparison.Ordinal));

    public string? LastBodyFor(HttpMethod method, string pathFragment)
    {
        for (var i = Requests.Count - 1; i >= 0; i--)
        {
            if (Requests[i].Method == method
                && Requests[i].RequestUri!.AbsolutePath.Contains(pathFragment, StringComparison.Ordinal))
            {
                return Bodies[i];
            }
        }

        return null;
    }

    public RoutingHandler OnGet(string pathFragment, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        On(HttpMethod.Get, pathFragment, _ => Respond(status, json));

    public RoutingHandler OnPost(string pathFragment, string json, HttpStatusCode status = HttpStatusCode.Created) =>
        On(HttpMethod.Post, pathFragment, _ => Respond(status, json));

    public RoutingHandler OnPostThrows(string pathFragment, Func<Exception> error) =>
        On(HttpMethod.Post, pathFragment, _ => throw error());

    public RoutingHandler OnGetThrows(string pathFragment, Func<Exception>? error = null) =>
        On(HttpMethod.Get, pathFragment, _ => throw (error ?? (() => new HttpRequestException("connection reset")))());

    /// <summary>Answers differently on each successive call — for lookup-then-create sequences.</summary>
    public RoutingHandler OnGetSequence(string pathFragment, params (HttpStatusCode Status, string Json)[] replies)
    {
        var calls = 0;
        return On(HttpMethod.Get, pathFragment, _ =>
        {
            var reply = replies[Math.Min(calls++, replies.Length - 1)];
            return Respond(reply.Status, reply.Json);
        });
    }

    private RoutingHandler On(HttpMethod method, string pathFragment, Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _routes.RemoveAll(r => r.Method == method && r.PathFragment == pathFragment);
        _routes.Add(new Route(method, pathFragment, responder));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

        var route = _routes.FirstOrDefault(r =>
            r.Method == request.Method
            && request.RequestUri!.AbsolutePath.Contains(r.PathFragment, StringComparison.Ordinal));

        if (route is null)
        {
            return Respond(HttpStatusCode.NotFound, $$"""{"errors": ["no stub for {{request.Method}} {{request.RequestUri!.AbsolutePath}}"]}""");
        }

        return route.Responder(request);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record Route(HttpMethod Method, string PathFragment, Func<HttpRequestMessage, HttpResponseMessage> Responder);
}
