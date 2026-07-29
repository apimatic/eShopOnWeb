using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Exercises <see cref="MaxioSubscriptionBillingService"/> through the SDK's HttpClient seam:
/// a queued stub handler answers each SDK call in order, so the tests assert real behaviour
/// (projection, idempotency, error translation, request shape) without any network.
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    private const string Reference = "demouser@microsoft.com";

    private sealed class QueuedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode status, string json)> _responses;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        public QueuedHandler((HttpStatusCode status, string json)[] responses)
            => _responses = new Queue<(HttpStatusCode, string)>(responses);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            var (status, json) = _responses.Count > 0
                ? _responses.Dequeue()
                : (HttpStatusCode.InternalServerError, "{}");

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private static (MaxioSubscriptionBillingService service, QueuedHandler handler) BuildService(
        (HttpStatusCode status, string json)[] responses, MaxioSettings? settings = null)
    {
        var handler = new QueuedHandler(responses);
        var options = new MaxioAdvancedBillingClientOptions { Environment = ServerEnvironment.Us };
        options.Server.Production.Us.Site = "test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);

        var service = new MaxioSubscriptionBillingService(
            client,
            Options.Create(settings ?? new MaxioSettings { ProductFamilyHandle = "eshop-subscribe" }),
            NullLogger<MaxioSubscriptionBillingService>.Instance);

        return (service, handler);
    }

    private static (HttpStatusCode, string) Ok(string json) => (HttpStatusCode.OK, json);

    private const string FamiliesJson = """[{"product_family":{"id":3023074,"handle":"eshop-subscribe"}}]""";

    [Fact]
    public async Task GetAvailablePlansAsync_ResolvesFamilyByHandle_AndMapsProducts()
    {
        var products = """
        [
          {"product":{"handle":"eshop-pro","name":"Pro Plan","description":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}},
          {"product":{"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}}
        ]
        """;
        var (service, handler) = BuildService(new[] { Ok(FamiliesJson), Ok(products) });

        var plans = await service.GetAvailablePlansAsync();

        Assert.Equal(2, plans.Count);
        var pro = Assert.Single(plans, p => p.Handle == "eshop-pro");
        Assert.Equal("Pro Plan", pro.Name);
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal(299m, pro.Price);
        Assert.Equal(1, pro.Interval);
        Assert.Equal("month", pro.IntervalUnit);

        // Family list is resolved before products are listed.
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
    }

    [Fact]
    public async Task SubscribeAsync_WhenCustomerAndSubscriptionAbsent_CreatesBoth_AndBillsByRemittance()
    {
        var created = """
        {"subscription":{"id":555,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},
         "current_billing_amount_in_cents":29900,"next_assessment_at":"2026-08-29T00:00:00Z"}}
        """;
        var (service, handler) = BuildService(new[]
        {
            (HttpStatusCode.NotFound, "{}"),                 // ReadCustomerByReference -> absent
            Ok("""{"customer":{"id":123}}"""),               // CreateCustomer
            Ok("[]"),                                        // ListCustomerSubscriptions -> none
            Ok(created)                                      // CreateSubscription
        });

        var result = await service.SubscribeAsync(new SubscribeRequest
        {
            UserReference = Reference,
            Email = Reference,
            ProductHandle = "eshop-pro"
        });

        Assert.False(result.AlreadyExisted);
        Assert.Equal(555, result.Subscription.Id);
        Assert.Equal("active", result.Subscription.State);
        Assert.Equal("eshop-pro", result.Subscription.ProductHandle);
        Assert.Equal(29900, result.Subscription.PriceInCents);
        Assert.Equal(new DateTime(2026, 8, 29), result.Subscription.NextBillingDate!.Value.UtcDateTime.Date);

        // The create-subscription POST must bill by remittance (no card) and carry handle + customer id.
        var createBody = handler.Bodies[^1];
        Assert.Equal(HttpMethod.Post, handler.Requests[^1].Method);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", createBody);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", createBody);
        Assert.Contains("\"customer_id\":123", createBody);
    }

    [Fact]
    public async Task SubscribeAsync_WhenLiveSubscriptionToSamePlanExists_IsIdempotent_AndDoesNotCreate()
    {
        var existing = """
        [{"subscription":{"id":93589822,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"},
          "current_billing_amount_in_cents":29900}}]
        """;
        var (service, handler) = BuildService(new[]
        {
            Ok("""{"customer":{"id":123,"reference":"demouser@microsoft.com"}}"""), // ReadCustomerByReference
            Ok(existing)                                                            // ListCustomerSubscriptions
        });

        var result = await service.SubscribeAsync(new SubscribeRequest
        {
            UserReference = Reference,
            Email = Reference,
            ProductHandle = "eshop-pro"
        });

        Assert.True(result.AlreadyExisted);
        Assert.Equal(93589822, result.Subscription.Id);

        // No subscription was created — a double-click must not double-enroll.
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task SubscribeAsync_WhenProviderRejectsWith422_ThrowsBillingExceptionCarrying422AndMessage()
    {
        var (service, _) = BuildService(new[]
        {
            (HttpStatusCode.NotFound, "{}"),
            Ok("""{"customer":{"id":123}}"""),
            Ok("[]"),
            (HttpStatusCode.UnprocessableEntity, """{"errors":["No payment method was on file for the $299.00 balance"]}""")
        });

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() =>
            service.SubscribeAsync(new SubscribeRequest { UserReference = Reference, Email = Reference, ProductHandle = "eshop-pro" }));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("No payment method", ex.Message);
    }

    [Fact]
    public async Task GetSubscriptionsForUserAsync_WhenCustomerDoesNotExist_ReturnsEmpty()
    {
        var (service, handler) = BuildService(new[] { (HttpStatusCode.NotFound, "{}") });

        var result = await service.GetSubscriptionsForUserAsync(Reference);

        Assert.Empty(result);
        Assert.Single(handler.Requests); // only the lookup, no subscription listing
    }

    [Fact]
    public async Task SubscribeAsync_WhenCustomerLookupFailsWithNon404_DoesNotTreatAsAbsent_AndSurfacesStatus()
    {
        // A 403 (non-retryable) must be a real failure, never silently read as "customer absent".
        var (service, handler) = BuildService(new[] { (HttpStatusCode.Forbidden, """{"error":"nope"}""") });

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() =>
            service.SubscribeAsync(new SubscribeRequest { UserReference = Reference, Email = Reference, ProductHandle = "eshop-pro" }));

        Assert.Equal(403, ex.StatusCode);
        Assert.DoesNotContain(handler.Requests, r => r.Method == HttpMethod.Post); // never attempted a create
    }
}
