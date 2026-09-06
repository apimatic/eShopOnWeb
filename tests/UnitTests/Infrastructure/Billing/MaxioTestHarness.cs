using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

/// <summary>
/// A fake provider. The <see cref="HttpClient"/> the SDK client is constructed from is the seam, so no
/// SDK internals are touched and no network call happens.
/// <para>
/// The routes below are the ones the SDK really calls — captured off the wire against the sandbox, not
/// guessed — so a stub that stops matching is a signal worth having rather than a broken test.
/// </para>
/// </summary>
internal sealed class MaxioStubHandler : HttpMessageHandler
{
    private readonly List<Func<HttpRequestMessage, HttpResponseMessage?>> _routes = new();

    /// <summary>Every request that reached the provider, in order. Retries append — this is what you count.</summary>
    public List<(HttpMethod Method, string Path, string Body)> Requests { get; } = new();

    public int CountOf(HttpMethod method, string pathFragment) =>
        Requests.Count(r => r.Method == method && r.Path.Contains(pathFragment, StringComparison.Ordinal));

    public string? BodyOf(HttpMethod method, string pathFragment) =>
        Requests.FirstOrDefault(r => r.Method == method && r.Path.Contains(pathFragment, StringComparison.Ordinal)).Body;

    public MaxioStubHandler On(HttpMethod method, string pathFragment, HttpStatusCode status, string json)
    {
        _routes.Add(request => Matches(request, method, pathFragment)
            ? new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            }
            : null);
        return this;
    }

    /// <summary>Registers a route that fails at the transport layer rather than answering.</summary>
    public MaxioStubHandler OnThrow(HttpMethod method, string pathFragment, Exception exception)
    {
        _routes.Add(request => Matches(request, method, pathFragment) ? throw exception : (HttpResponseMessage?)null);
        return this;
    }

    /// <summary>Registers a route whose answer changes per call, so a reconcile can differ from the write.</summary>
    public MaxioStubHandler OnSequence(HttpMethod method, string pathFragment, params (HttpStatusCode Status, string Json)[] answers)
    {
        var calls = 0;
        _routes.Add(request =>
        {
            if (!Matches(request, method, pathFragment))
            {
                return null;
            }

            var answer = answers[Math.Min(calls++, answers.Length - 1)];
            return new HttpResponseMessage(answer.Status)
            {
                Content = new StringContent(answer.Json, System.Text.Encoding.UTF8, "application/json")
            };
        });
        return this;
    }

    private static bool Matches(HttpRequestMessage request, HttpMethod method, string pathFragment) =>
        request.Method == method
        && (request.RequestUri?.AbsolutePath.Contains(pathFragment, StringComparison.Ordinal) ?? false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri?.AbsolutePath ?? string.Empty, body));

        foreach (var route in _routes)
        {
            var response = route(request);
            if (response is not null)
            {
                return response;
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"{{\"unstubbed\":\"{request.Method} {request.RequestUri?.AbsolutePath}\"}}",
                System.Text.Encoding.UTF8, "application/json")
        };
    }
}

internal static class MaxioTestHarness
{
    // The real wire paths, verified against the sandbox.
    public const string ProductFamiliesPath = "/product_families.json";
    public const string ProductsPath = "/products.json";
    public const string CustomerLookupPath = "/customers/lookup.json";
    public const string CustomersPath = "/customers.json";
    public const string CustomerSubscriptionsPath = "/subscriptions.json";
    public const string SubscriptionsPath = "/subscriptions.json";

    public const string FamilyHandle = "eshop-subscribe";
    public const int FamilyId = 42;
    public const int CustomerId = 7;
    public const string SubscriberEmail = "demouser@microsoft.com";

    public static MaxioSettings Settings() => new()
    {
        ApiKey = "test-key",
        Subdomain = "test-site",
        ProductFamilyHandle = FamilyHandle,
        Currency = "USD"
    };

    /// <summary>
    /// Builds the service over the same handler chain the application registers, so the write-once guard
    /// is exercised exactly as it is in production rather than being bypassed by the test.
    /// </summary>
    public static MaxioSubscriptionBillingService ServiceOver(MaxioStubHandler stub, MaxioSettings? settings = null)
    {
        settings ??= Settings();
        var writeOnce = new MaxioWriteOnceHandler { InnerHandler = stub };
        var client = new MaxioAdvancedBillingClient(new HttpClient(writeOnce), MaxioBillingRegistration.BuildClientOptions(settings));
        return new MaxioSubscriptionBillingService(client, settings, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    public static MaxioStubHandler WithFamily(this MaxioStubHandler stub) =>
        stub.On(HttpMethod.Get, ProductFamiliesPath, HttpStatusCode.OK,
            """[{"product_family":{"id":@ID@,"handle":"@HANDLE@","name":"eShop Subscribe"}}]"""
                .Replace("@ID@", FamilyId.ToString(CultureInfo.InvariantCulture))
                .Replace("@HANDLE@", FamilyHandle));

    public static MaxioStubHandler WithPlans(this MaxioStubHandler stub, string json) =>
        stub.On(HttpMethod.Get, $"/product_families/{FamilyId}/products.json", HttpStatusCode.OK, json);

    /// <summary>The two seeded plans, plus an archived one that must never be offered.</summary>
    public static MaxioStubHandler WithSeededPlans(this MaxioStubHandler stub) => stub.WithPlans(
        """
        [
          {"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","description":"Everything, monthly.",
                      "price_in_cents":29900,"interval":1,"interval_unit":"month","archived_at":null,
                      "require_credit_card":false,"request_credit_card":true}},
          {"product":{"id":2,"handle":"basic-plan","name":"Basic Plan","description":"The essentials.",
                      "price_in_cents":2900,"interval":1,"interval_unit":"month","archived_at":null,
                      "require_credit_card":true,"request_credit_card":true}},
          {"product":{"id":3,"handle":"retired-plan","name":"Retired Plan","description":"Gone.",
                      "price_in_cents":9900,"interval":1,"interval_unit":"month",
                      "archived_at":"2025-01-01T00:00:00Z","require_credit_card":false}}
        ]
        """);

    public static MaxioStubHandler WithExistingCustomer(this MaxioStubHandler stub) =>
        stub.On(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.OK, CustomerJson());

    public static MaxioStubHandler WithNoCustomer(this MaxioStubHandler stub) =>
        stub.On(HttpMethod.Get, CustomerLookupPath, HttpStatusCode.NotFound, """{"errors":["Customer not found"]}""");

    public static MaxioStubHandler WithCustomerCreated(this MaxioStubHandler stub) =>
        stub.On(HttpMethod.Post, CustomersPath, HttpStatusCode.Created, CustomerJson());

    private static string CustomerJson() =>
        """{"customer":{"id":@ID@,"email":"@EMAIL@","reference":"eshoponweb-x"}}"""
            .Replace("@ID@", CustomerId.ToString(CultureInfo.InvariantCulture))
            .Replace("@EMAIL@", SubscriberEmail);

    public static MaxioStubHandler WithCustomerSubscriptions(this MaxioStubHandler stub, string json) =>
        stub.On(HttpMethod.Get, $"/customers/{CustomerId}/subscriptions.json", HttpStatusCode.OK, json);

    public static MaxioStubHandler WithNoSubscriptions(this MaxioStubHandler stub) =>
        stub.WithCustomerSubscriptions("[]");

    public static string SubscriptionJson(
        int id = 900, string handle = "eshop-pro", string state = "active", long priceInCents = 29900) =>
        """
        {"subscription":{"id":@ID@,"state":"@STATE@","balance_in_cents":0,
          "product_price_in_cents":@PRICE@,"currency":"USD",
          "current_period_ends_at":"2026-10-06T00:00:00Z","next_assessment_at":"2026-10-07T00:00:00Z",
          "activated_at":"2026-09-06T00:00:00Z",
          "product":{"id":1,"handle":"@HANDLE@","name":"Pro Plan","price_in_cents":@PRICE@,
                     "interval":1,"interval_unit":"month"}}}
        """
            .Replace("@ID@", id.ToString(CultureInfo.InvariantCulture))
            .Replace("@STATE@", state)
            .Replace("@PRICE@", priceInCents.ToString(CultureInfo.InvariantCulture))
            .Replace("@HANDLE@", handle);

    public static string SubscriptionListJson(
        int id = 900, string handle = "eshop-pro", string state = "active") =>
        $"[{SubscriptionJson(id, handle, state)}]";
}
