using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.PublicApi.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi.Billing;

/// <summary>
/// Tests the Maxio-backed subscription service through the SDK's HttpClient seam:
/// every provider response is stubbed, no network happens. Asserts the
/// idempotency flow, the error mapping and — for the transport-fault case — that
/// exactly one POST reaches the wire even though the SDK's retry pipeline retries
/// transport failures on POST.
/// </summary>
public class MaxioSubscriptionServiceTests
{
    private const string ProductJson = """{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","product_family":{"id":9,"handle":"eshop-subscribe","name":"eShop Subscribe"}}}""";

    private const string ProductOutsideFamilyJson = """{"product":{"id":7,"name":"Other","handle":"other-plan","price_in_cents":500,"interval":1,"interval_unit":"month","product_family":{"id":3,"handle":"some-other-family","name":"Other"}}}""";

    private const string CustomerJson = """{"customer":{"id":123,"reference":"eshop-user-u1","email":"demouser@microsoft.com","first_name":"Demouser","last_name":"User"}}""";

    private const string SubscriptionJson = """{"subscription":{"id":55,"reference":"eshop-sub-u1-eshop-pro","state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-11-01T00:00:00Z","next_assessment_at":"2026-11-01T00:00:00Z","customer":{"id":123,"reference":"eshop-user-u1"},"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}""";

    private const string FamiliesJson = """[{"product_family":{"id":9,"handle":"eshop-subscribe","name":"eShop Subscribe"}}]""";

    private const string FamilyProductsJson = """[{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}},{"product":{"id":2,"name":"Basic Plan","handle":"basic-plan","price_in_cents":2900,"interval":1,"interval_unit":"month","archived_at":"2026-01-01T00:00:00Z"}}]""";

    private static readonly SubscriberProfile Profile = new("u1", "demouser@microsoft.com", "Demouser", "User");

    private sealed class RecordingStub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _route;

        public List<HttpRequestMessage> Requests { get; } = new();

        // The SDK disposes request content after sending, so bodies are captured during send.
        public Dictionary<HttpRequestMessage, string> Bodies { get; } = new();

        public RecordingStub(Func<HttpRequestMessage, HttpResponseMessage> route) => _route = route;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                Bodies[request] = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            }
            return Task.FromResult(_route(request));
        }
    }

    private static MaxioSubscriptionService CreateService(RecordingStub stub) =>
        new(CreateClient(stub),
            Options.Create(new MaxioSettings { ProductFamilyHandle = "eshop-subscribe", ApiKey = "k", Subdomain = "test" }),
            NullLogger<MaxioSubscriptionService>.Instance);

    private static MaxioAdvancedBillingClient CreateClient(RecordingStub stub)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" },
            Environment = ServerEnvironment.Us
        };
        options.Server.Production.Us.Site = "test";
        // Same shape as the DI pipeline: the single-send guard sits between the SDK's
        // retry pipeline and the wire, so the write-once behaviour is exercised.
        return new MaxioAdvancedBillingClient(new HttpClient(new SingleSendHandler { InnerHandler = stub }), options);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Empty(HttpStatusCode status) => new(status);

    private static bool IsFamilyList(HttpRequestMessage r) =>
        r.RequestUri!.AbsolutePath.Contains("product_families") && !r.RequestUri.AbsolutePath.Contains("products");

    private static bool IsFamilyProducts(HttpRequestMessage r) =>
        r.RequestUri!.AbsolutePath.Contains("product_families") && r.RequestUri.AbsolutePath.Contains("products");

    private static bool IsProductByHandle(HttpRequestMessage r) =>
        !r.RequestUri!.AbsolutePath.Contains("product_families") && r.RequestUri.AbsolutePath.Contains("products");

    private static bool IsCustomerByReference(HttpRequestMessage r) =>
        r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("customers") && !r.RequestUri.AbsolutePath.Contains("subscriptions");

    private static bool IsCreateCustomer(HttpRequestMessage r) =>
        r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("customers");

    private static bool IsFindSubscription(HttpRequestMessage r) =>
        r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("subscription") && r.RequestUri.Query.Contains("reference=");

    private static bool IsListCustomerSubscriptions(HttpRequestMessage r) =>
        r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("customers") && r.RequestUri.AbsolutePath.Contains("subscriptions");

    private static bool IsCreateSubscription(HttpRequestMessage r) =>
        r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.Contains("subscription");

    [Fact]
    public async Task GetAvailablePlans_MapsNonArchivedProductsOfConfiguredFamily()
    {
        var stub = new RecordingStub(r =>
            IsFamilyProducts(r) ? Json(HttpStatusCode.OK, FamilyProductsJson) :
            IsFamilyList(r) ? Json(HttpStatusCode.OK, FamiliesJson) :
            Empty(HttpStatusCode.NotFound));

        var plans = await CreateService(stub).GetAvailablePlansAsync(CancellationToken.None);

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal("Pro Plan", plan.Name);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(1, plan.Interval);
        Assert.Equal("month", plan.IntervalUnit);
    }

    [Fact]
    public async Task SubscribeAsync_WhenNoCustomerAndNoSubscription_CreatesBoth()
    {
        var stub = new RecordingStub(r =>
            IsProductByHandle(r) ? Json(HttpStatusCode.OK, ProductJson) :
            IsCustomerByReference(r) ? Empty(HttpStatusCode.NotFound) :
            IsCreateCustomer(r) ? Json(HttpStatusCode.Created, CustomerJson) :
            IsFindSubscription(r) ? Empty(HttpStatusCode.NotFound) :
            IsCreateSubscription(r) ? Json(HttpStatusCode.Created, SubscriptionJson) :
            Empty(HttpStatusCode.NotFound));

        var result = await CreateService(stub).SubscribeAsync(Profile, "eshop-pro", CancellationToken.None);

        Assert.Equal(55, result.SubscriptionId);
        Assert.Equal("eshop-sub-u1-eshop-pro", result.Reference);
        Assert.Equal("active", result.State);
        Assert.Equal("eshop-pro", result.PlanHandle);
        Assert.Equal(29900, result.PriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), result.NextBillingDate);
        Assert.Equal(123, result.CustomerId);

        var subscriptionPost = Assert.Single(stub.Requests, r => IsCreateSubscription(r));
        var body = stub.Bodies[subscriptionPost];
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":123", body);
        Assert.Contains("\"reference\":\"eshop-sub-u1-eshop-pro\"", body);

        var customerPost = Assert.Single(stub.Requests, r => IsCreateCustomer(r));
        var customerBody = stub.Bodies[customerPost];
        Assert.Contains("\"reference\":\"eshop-user-u1\"", customerBody);
        Assert.Contains("\"first_name\"", customerBody);
    }

    [Fact]
    public async Task SubscribeAsync_WhenSubscriptionAlreadyExists_DoesNotCreateAgain()
    {
        var stub = new RecordingStub(r =>
            IsProductByHandle(r) ? Json(HttpStatusCode.OK, ProductJson) :
            IsCustomerByReference(r) ? Json(HttpStatusCode.OK, CustomerJson) :
            IsFindSubscription(r) ? Json(HttpStatusCode.OK, SubscriptionJson) :
            Empty(HttpStatusCode.NotFound));

        var result = await CreateService(stub).SubscribeAsync(Profile, "eshop-pro", CancellationToken.None);

        Assert.Equal(55, result.SubscriptionId);
        Assert.DoesNotContain(stub.Requests, IsCreateSubscription);
        Assert.DoesNotContain(stub.Requests, IsCreateCustomer);
    }

    [Fact]
    public async Task SubscribeAsync_UnknownPlan_Returns404()
    {
        var stub = new RecordingStub(r =>
            IsProductByHandle(r) ? Empty(HttpStatusCode.NotFound) :
            Empty(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => CreateService(stub).SubscribeAsync(Profile, "nope", CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task SubscribeAsync_PlanOutsideConfiguredFamily_Returns404()
    {
        var stub = new RecordingStub(r =>
            IsProductByHandle(r) ? Json(HttpStatusCode.OK, ProductOutsideFamilyJson) :
            Empty(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => CreateService(stub).SubscribeAsync(Profile, "other-plan", CancellationToken.None));

        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task SubscribeAsync_ProviderValidation422_SurfacesStatusAndErrors()
    {
        var stub = new RecordingStub(r =>
            IsProductByHandle(r) ? Json(HttpStatusCode.OK, ProductJson) :
            IsCustomerByReference(r) ? Json(HttpStatusCode.OK, CustomerJson) :
            IsFindSubscription(r) ? Empty(HttpStatusCode.NotFound) :
            IsCreateSubscription(r) ? Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Card required","Bad request"]}""") :
            Empty(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => CreateService(stub).SubscribeAsync(Profile, "eshop-pro", CancellationToken.None));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("Card required", ex.Errors);
        Assert.Contains("Bad request", ex.Errors);
    }

    [Fact]
    public async Task SubscribeAsync_TransportFaultOnCreate_ReconcilesByReferenceAndNeverResends()
    {
        var findCalls = 0;
        var stub = new RecordingStub(r =>
        {
            if (IsProductByHandle(r)) return Json(HttpStatusCode.OK, ProductJson);
            if (IsCustomerByReference(r)) return Json(HttpStatusCode.OK, CustomerJson);
            if (IsFindSubscription(r))
            {
                findCalls++;
                return findCalls == 1 ? Empty(HttpStatusCode.NotFound) : Json(HttpStatusCode.OK, SubscriptionJson);
            }
            if (IsCreateSubscription(r))
            {
                // The bytes may have reached the provider before the connection reset.
                throw new HttpRequestException("connection reset");
            }
            return Empty(HttpStatusCode.NotFound);
        });

        var result = await CreateService(stub).SubscribeAsync(Profile, "eshop-pro", CancellationToken.None);

        // The single allowed send did take effect; reconciliation found it instead of creating twice.
        Assert.Equal(55, result.SubscriptionId);
        Assert.Equal(1, stub.Requests.Count(IsCreateSubscription));
        Assert.Equal(2, findCalls);
    }

    [Fact]
    public async Task SubscribeAsync_TransportFaultOnCreate_WithNoReconciledSubscription_Surfaces503()
    {
        var stub = new RecordingStub(r =>
            IsProductByHandle(r) ? Json(HttpStatusCode.OK, ProductJson) :
            IsCustomerByReference(r) ? Json(HttpStatusCode.OK, CustomerJson) :
            IsFindSubscription(r) ? Empty(HttpStatusCode.NotFound) :
            IsCreateSubscription(r) ? throw new HttpRequestException("connection reset") :
            Empty(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<MaxioBillingException>(
            () => CreateService(stub).SubscribeAsync(Profile, "eshop-pro", CancellationToken.None));

        Assert.Equal(503, ex.StatusCode);
        Assert.Equal(1, stub.Requests.Count(IsCreateSubscription));
    }

    [Fact]
    public async Task GetSubscriptionsForUser_WithoutProvisionedCustomer_ReturnsEmpty()
    {
        var stub = new RecordingStub(r =>
            IsCustomerByReference(r) ? Empty(HttpStatusCode.NotFound) :
            Empty(HttpStatusCode.NotFound));

        var subscriptions = await CreateService(stub).GetSubscriptionsForUserAsync(Profile, CancellationToken.None);

        Assert.Empty(subscriptions);
        Assert.DoesNotContain(stub.Requests, IsListCustomerSubscriptions);
    }

    [Fact]
    public async Task GetSubscriptionsForUser_ListsCustomerSubscriptions()
    {
        var listJson = $"[{SubscriptionJson}]";
        var stub = new RecordingStub(r =>
            IsListCustomerSubscriptions(r) ? Json(HttpStatusCode.OK, listJson) :
            IsCustomerByReference(r) ? Json(HttpStatusCode.OK, CustomerJson) :
            Empty(HttpStatusCode.NotFound));

        var subscriptions = await CreateService(stub).GetSubscriptionsForUserAsync(Profile, CancellationToken.None);

        var summary = Assert.Single(subscriptions);
        Assert.Equal(55, summary.SubscriptionId);
        Assert.Equal("active", summary.State);
        Assert.Equal(29900, summary.PriceInCents);
    }
}