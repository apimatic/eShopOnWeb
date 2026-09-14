using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.SubscriptionTests;

/// <summary>
/// Captures every outgoing request and answers from a supplied responder — the HttpClient seam the
/// Maxio SDK is built to be tested through (no real network).
/// </summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> Bodies { get; } = new();

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        return _responder(request);
    }
}

/// <summary>
/// A canned Maxio site: routes the SDK's requests to fixture responses and records how many times
/// the create endpoints were hit, so idempotency can be asserted on the wire.
/// </summary>
public sealed class FakeMaxio
{
    // A neutral fixture handle — deliberately NOT the real configured family handle.
    public const string FamilyHandle = "test-family";

    public bool CustomerFound { get; set; }
    public bool HasActiveProSubscription { get; set; }
    public HttpStatusCode CreateSubscriptionStatus { get; set; } = HttpStatusCode.Created;
    public string? CreateSubscriptionBodyOverride { get; set; }

    public int CreateCustomerCalls { get; private set; }
    public int CreateSubscriptionCalls { get; private set; }

    public HttpResponseMessage Respond(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath.ToLowerInvariant();
        var method = request.Method;

        if (method == HttpMethod.Get && path.Contains("product_families") && !path.Contains("/products"))
            return Json(HttpStatusCode.OK, Families);

        if (method == HttpMethod.Get && path.Contains("product_families") && path.Contains("/products"))
            return Json(HttpStatusCode.OK, Products);

        if (method == HttpMethod.Get && path.Contains("lookup"))
            return CustomerFound ? Json(HttpStatusCode.OK, Customer) : Json(HttpStatusCode.NotFound, "{}");

        if (method == HttpMethod.Post && path.Contains("customers") && !path.Contains("subscriptions"))
        {
            CreateCustomerCalls++;
            CustomerFound = true; // The site now knows this customer (models the created record).
            return Json(HttpStatusCode.Created, Customer);
        }

        if (method == HttpMethod.Get && path.Contains("/customers/") && path.Contains("subscriptions"))
            return Json(HttpStatusCode.OK, HasActiveProSubscription ? ActiveSubscriptions : "[]");

        if (method == HttpMethod.Post && path.Contains("subscriptions"))
        {
            CreateSubscriptionCalls++;
            if (CreateSubscriptionStatus == HttpStatusCode.Created || CreateSubscriptionStatus == HttpStatusCode.OK)
                HasActiveProSubscription = true; // Subsequent lookups see the new subscription.
            return Json(CreateSubscriptionStatus, CreateSubscriptionBodyOverride ?? CreatedSubscription);
        }

        return Json(HttpStatusCode.InternalServerError, $"{{\"unmatched\":\"{method} {path}\"}}");
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private const string Families =
        """[{"product_family":{"id":999,"handle":"test-family","name":"Test Family"}}]""";

    private const string Products =
        """[{"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}},{"product":{"id":2,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}}]""";

    private const string Customer =
        """{"customer":{"id":555,"reference":"shopper@example.com","first_name":"shopper","last_name":"eShopOnWeb","email":"shopper@example.com"}}""";

    private const string ActiveSubscriptions =
        """[{"subscription":{"id":42,"state":"active","product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900},"product_price_in_cents":29900,"current_period_ends_at":"2026-09-15T00:00:00Z","next_assessment_at":"2026-09-15T00:00:00Z"}}]""";

    private const string CreatedSubscription =
        """{"subscription":{"id":77,"state":"active","product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900},"product_price_in_cents":29900,"current_period_ends_at":"2026-09-15T00:00:00Z","next_assessment_at":"2026-09-15T00:00:00Z"}}""";

    public const string SubscriptionValidationError =
        """{"errors":["No payment method was on file for the $299.00 balance"]}""";
}

internal static class MaxioTestHarness
{
    public static MaxioBillingService Build(StubHandler handler, string? paymentCollectionMethod = "remittance")
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        };
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);

        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test-site",
            ProductFamilyHandle = FakeMaxio.FamilyHandle,
            PaymentCollectionMethod = paymentCollectionMethod
        });

        return new MaxioBillingService(
            client,
            settings,
            new KeyedAsyncLock(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MaxioBillingService>.Instance);
    }
}
