using System.Net;
using Microsoft.eShopWeb.PublicApi.Billing;
using Xunit;

namespace Microsoft.eShopWeb.PublicApiUnitTests.Billing;

public class MaxioBillingGatewayTests
{
    private const string ProductsPath = "/product_families/handle:eshop-subscribe/products.json";

    [Fact]
    public async Task ListPlansReturnsNonArchivedPlansFromTheConfiguredFamilyUsingBasicAuthAndTheBaseUrlOverride()
    {
        var stub = new StubHandler().OnJson(HttpMethod.Get, ProductsPath, HttpStatusCode.OK, MaxioJson.StandardPlans());
        var gateway = MaxioTestFactory.Gateway(stub);

        var catalog = await gateway.ListPlansAsync(CancellationToken.None);

        Assert.False(catalog.Truncated);
        Assert.Equal(new[] { "eshop-pro", "basic-plan" }, catalog.Plans.Select(p => p.Handle));
        var pro = catalog.Plans[0];
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.Equal(1, pro.Interval);

        var request = Assert.Single(stub.Requests);
        Assert.StartsWith(MaxioTestFactory.BaseUrl + "/", request.RequestUri!.ToString());
        Assert.Contains("per_page=200", request.RequestUri.Query);
        Assert.Contains("page=1", request.RequestUri.Query);
        Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task ListPlansDerivesTheBaseAddressFromTheSubdomainWhenNoOverrideIsSet()
    {
        var settings = MaxioTestFactory.Settings();
        settings.BaseUrl = null;
        settings.Subdomain = "my-site";
        var stub = new StubHandler().OnJson(HttpMethod.Get, ProductsPath, HttpStatusCode.OK, MaxioJson.StandardPlans());

        await MaxioTestFactory.Gateway(stub, settings: settings).ListPlansAsync(CancellationToken.None);

        Assert.Equal("my-site.chargify.com", stub.Requests.Single().RequestUri!.Host);
    }

    [Fact]
    public async Task ListPlansStopsAtThePageCapAndReportsTruncated()
    {
        // Every page is full, so only the cap can stop the loop.
        var stub = new StubHandler().OnJson(HttpMethod.Get, ProductsPath, HttpStatusCode.OK, MaxioJson.Products(
            MaxioJson.Product(1, "plan-a", "A", 100),
            MaxioJson.Product(2, "plan-b", "B", 200)));
        var gateway = MaxioTestFactory.Gateway(stub, new MaxioGatewayLimits { PlansPerPage = 2, MaxPlanPages = 3 });

        var catalog = await gateway.ListPlansAsync(CancellationToken.None);

        Assert.True(catalog.Truncated);
        Assert.Equal(3, stub.Requests.Count);
        Assert.Contains("page=3", stub.Requests[2].RequestUri!.Query);
    }

    [Fact]
    public async Task ListPlansStopsOnAShortPageWithoutTruncation()
    {
        var calls = 0;
        var stub = new StubHandler().On(HttpMethod.Get, ProductsPath, _ => StubHandler.Json(HttpStatusCode.OK,
            ++calls == 1
                ? MaxioJson.Products(MaxioJson.Product(1, "plan-a", "A", 100), MaxioJson.Product(2, "plan-b", "B", 200))
                : MaxioJson.Products(MaxioJson.Product(3, "plan-c", "C", 300))));
        var gateway = MaxioTestFactory.Gateway(stub, new MaxioGatewayLimits { PlansPerPage = 2, MaxPlanPages = 5 });

        var catalog = await gateway.ListPlansAsync(CancellationToken.None);

        Assert.False(catalog.Truncated);
        Assert.Equal(3, catalog.Plans.Count);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task UnknownProductFamilyIsAConfigurationFailureNotACallerError()
    {
        var stub = new StubHandler().OnJson(HttpMethod.Get, ProductsPath, HttpStatusCode.NotFound, "\"Not found\"");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => MaxioTestFactory.Gateway(stub).ListPlansAsync(CancellationToken.None));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
        Assert.DoesNotContain("stub.maxio.test", ex.Message);
    }

    [Fact]
    public async Task RejectedCredentialsSurfaceAsUnavailableWithoutLeakingDetails()
    {
        var stub = new StubHandler().OnJson(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.Unauthorized, "{}");

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() =>
            MaxioTestFactory.Gateway(stub).FindCustomerByReferenceAsync("eshop-user-x", CancellationToken.None));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
        Assert.Equal(401, ex.ProviderStatus);
        Assert.DoesNotContain("test-placeholder-key", ex.Message);
    }

    [Fact]
    public async Task FindCustomerReturnsNullOnNotFound()
    {
        var stub = new StubHandler().OnJson(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, "{}");

        Assert.Null(await MaxioTestFactory.Gateway(stub).FindCustomerByReferenceAsync("eshop-user-x", CancellationToken.None));
        Assert.Contains("reference=", stub.Requests.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task CreateCustomerSendsTheReferenceAndReturnsTheNewId()
    {
        var stub = new StubHandler().OnJson(HttpMethod.Post, "/customers.json", HttpStatusCode.Created, MaxioJson.Customer(42, "eshop-user-x"));

        var outcome = await MaxioTestFactory.Gateway(stub).CreateCustomerAsync(
            new NewBillingCustomer("eshop-user-x", "Jane", "Doe", "jane.doe@example.com"), CancellationToken.None);

        Assert.Equal(WriteOutcomeKind.Created, outcome.Kind);
        Assert.Equal(42, outcome.Value!.Id);
        var body = stub.Bodies.Single()!;
        Assert.Contains("\"reference\":\"eshop-user-x\"", body);
        Assert.Contains("\"first_name\":\"Jane\"", body);
    }

    [Fact]
    public async Task CreateCustomerConnectionFailureIsSettledByReReadingByReference()
    {
        var stub = new StubHandler()
            .On(HttpMethod.Post, "/customers.json", _ => throw new HttpRequestException("connection reset"))
            .OnJson(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioJson.Customer(42, "eshop-user-x"));

        var outcome = await MaxioTestFactory.Gateway(stub).CreateCustomerAsync(
            new NewBillingCustomer("eshop-user-x", "Jane", "Doe", "jane.doe@example.com"), CancellationToken.None);

        Assert.Equal(WriteOutcomeKind.Created, outcome.Kind);
        Assert.Equal(42, outcome.Value!.Id);
        Assert.Equal(1, stub.Count(HttpMethod.Post, "/customers.json")); // the SDK never resends the POST
        var reRead = stub.Requests.Last();
        Assert.Equal(HttpMethod.Get, reRead.Method);
        Assert.Equal("/customers/lookup.json", reRead.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CreateCustomerBudgetExpiryIsSettledOnAFreshBudget()
    {
        var stub = new StubHandler()
            .On(HttpMethod.Post, "/customers.json", async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct); // provider hangs until our total budget fires
                return StubHandler.Json(HttpStatusCode.Created, "{}");
            })
            .OnJson(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioJson.Customer(42, "eshop-user-x"));
        var gateway = MaxioTestFactory.Gateway(stub, new MaxioGatewayLimits { TotalBudget = TimeSpan.FromMilliseconds(200) });

        var outcome = await gateway.CreateCustomerAsync(
            new NewBillingCustomer("eshop-user-x", "Jane", "Doe", "jane.doe@example.com"), CancellationToken.None);

        Assert.Equal(WriteOutcomeKind.Created, outcome.Kind);
        Assert.Equal(42, outcome.Value!.Id);
    }

    [Fact]
    public async Task CreateCustomerConnectionFailureWithNoCustomerFoundIsUnknownNotFailed()
    {
        var stub = new StubHandler()
            .On(HttpMethod.Post, "/customers.json", _ => throw new HttpRequestException("connection reset"))
            .OnJson(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, "{}");

        var outcome = await MaxioTestFactory.Gateway(stub).CreateCustomerAsync(
            new NewBillingCustomer("eshop-user-x", "Jane", "Doe", "jane.doe@example.com"), CancellationToken.None);

        Assert.Equal(WriteOutcomeKind.Unknown, outcome.Kind);
    }

    [Fact]
    public async Task CreateCustomer422WhoseReferenceAlreadyExistsIsAlreadyExisted()
    {
        var stub = new StubHandler()
            .OnJson(HttpMethod.Post, "/customers.json", HttpStatusCode.UnprocessableEntity, """{"errors":["Reference must be unique"]}""")
            .OnJson(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.OK, MaxioJson.Customer(7, "eshop-user-x"));

        var outcome = await MaxioTestFactory.Gateway(stub).CreateCustomerAsync(
            new NewBillingCustomer("eshop-user-x", "Jane", "Doe", "jane.doe@example.com"), CancellationToken.None);

        Assert.Equal(WriteOutcomeKind.AlreadyExisted, outcome.Kind);
        Assert.Equal(7, outcome.Value!.Id);
    }

    [Fact]
    public async Task CreateCustomer422WithNothingFoundIsRefusedWithProviderMessages()
    {
        var stub = new StubHandler()
            .OnJson(HttpMethod.Post, "/customers.json", HttpStatusCode.UnprocessableEntity, """{"errors":["Email address: is invalid"]}""")
            .OnJson(HttpMethod.Get, "/customers/lookup.json", HttpStatusCode.NotFound, "{}");

        var outcome = await MaxioTestFactory.Gateway(stub).CreateCustomerAsync(
            new NewBillingCustomer("eshop-user-x", "Jane", "Doe", "bad"), CancellationToken.None);

        Assert.Equal(WriteOutcomeKind.Refused, outcome.Kind);
        Assert.Equal(422, outcome.ProviderStatus);
        Assert.Contains("Email address: is invalid", outcome.Messages);
    }

    [Fact]
    public async Task CreateSubscriptionSendsHandleCustomerAndReference()
    {
        var stub = new StubHandler().OnJson(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, MaxioJson.Subscription(500, "eshop-sub-1"));

        var outcome = await MaxioTestFactory.Gateway(stub).CreateSubscriptionAsync(
            new NewBillingSubscription("eshop-sub-1", 42, "eshop-pro", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(WriteOutcomeKind.Created, outcome.Kind);
        Assert.Equal(500, outcome.Value!.Id);
        Assert.Equal("active", outcome.Value.State);
        Assert.Equal("USD", outcome.Value.Currency);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T10:00:00Z"), outcome.Value.NextBillingAt);
        var body = stub.Bodies.Single()!;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":42", body);
        Assert.Contains("\"reference\":\"eshop-sub-1\"", body);
        // No card capture in this flow: automatic collection was refused live (422, no payment method on file).
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        Assert.Equal("remittance", outcome.Value.PaymentCollectionMethod);
    }

    [Fact]
    public async Task CreateSubscriptionConnectionFailureIsSettledByFindSubscription()
    {
        var stub = new StubHandler()
            .On(HttpMethod.Post, "/subscriptions.json", _ => throw new HttpRequestException("connection reset"))
            .OnJson(HttpMethod.Get, "/subscriptions/lookup.json", HttpStatusCode.OK, MaxioJson.Subscription(500, "eshop-sub-1"));

        var outcome = await MaxioTestFactory.Gateway(stub).CreateSubscriptionAsync(
            new NewBillingSubscription("eshop-sub-1", 42, "eshop-pro", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(WriteOutcomeKind.Created, outcome.Kind);
        Assert.Equal(500, outcome.Value!.Id);
        Assert.Equal(1, stub.Count(HttpMethod.Post, "/subscriptions.json"));
        Assert.Contains("reference=", stub.Requests.Last().RequestUri!.Query);
    }

    [Fact]
    public async Task CreateSubscriptionSettlementFallsBackToTheCustomersListWhenTheReferenceIsNotIndexed()
    {
        var attempt = DateTimeOffset.Parse("2026-10-20T10:00:00Z");
        var stub = new StubHandler()
            .On(HttpMethod.Post, "/subscriptions.json", _ => throw new HttpRequestException("connection reset"))
            .OnJson(HttpMethod.Get, "/subscriptions/lookup.json", HttpStatusCode.NotFound, "")
            .OnJson(HttpMethod.Get, "/customers/42/subscriptions.json", HttpStatusCode.OK,
                MaxioJson.List(MaxioJson.Subscription(501, null, createdAt: "2026-10-20T10:00:05Z")));

        var outcome = await MaxioTestFactory.Gateway(stub).CreateSubscriptionAsync(
            new NewBillingSubscription("eshop-sub-1", 42, "eshop-pro", attempt), CancellationToken.None);

        Assert.Equal(WriteOutcomeKind.Created, outcome.Kind);
        Assert.Equal(501, outcome.Value!.Id);
    }

    [Fact]
    public async Task CreateSubscriptionConnectionFailureWithNothingFoundIsUnknown()
    {
        var stub = new StubHandler()
            .On(HttpMethod.Post, "/subscriptions.json", _ => throw new HttpRequestException("connection reset"))
            .OnJson(HttpMethod.Get, "/subscriptions/lookup.json", HttpStatusCode.NotFound, "")
            .OnJson(HttpMethod.Get, "/customers/42/subscriptions.json", HttpStatusCode.OK, "[]");

        var outcome = await MaxioTestFactory.Gateway(stub).CreateSubscriptionAsync(
            new NewBillingSubscription("eshop-sub-1", 42, "eshop-pro", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(WriteOutcomeKind.Unknown, outcome.Kind);
    }

    [Fact]
    public async Task CreateSubscriptionUnreadableSuccessBodyIsSettledNotReportedAsFailure()
    {
        var stub = new StubHandler()
            .OnJson(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.Created, """{"subscription":"not-an-object"}""")
            .OnJson(HttpMethod.Get, "/subscriptions/lookup.json", HttpStatusCode.OK, MaxioJson.Subscription(500, "eshop-sub-1"));

        var outcome = await MaxioTestFactory.Gateway(stub).CreateSubscriptionAsync(
            new NewBillingSubscription("eshop-sub-1", 42, "eshop-pro", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(WriteOutcomeKind.Created, outcome.Kind);
        Assert.Equal(500, outcome.Value!.Id);
    }

    [Fact]
    public async Task CreateSubscription422IsRefusedWithProviderMessages()
    {
        var stub = new StubHandler()
            .OnJson(HttpMethod.Post, "/subscriptions.json", HttpStatusCode.UnprocessableEntity, """{"errors":["Product must be active"]}""")
            .OnJson(HttpMethod.Get, "/subscriptions/lookup.json", HttpStatusCode.NotFound, "")
            .OnJson(HttpMethod.Get, "/customers/42/subscriptions.json", HttpStatusCode.OK, "[]");

        var outcome = await MaxioTestFactory.Gateway(stub).CreateSubscriptionAsync(
            new NewBillingSubscription("eshop-sub-1", 42, "eshop-pro", DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(WriteOutcomeKind.Refused, outcome.Kind);
        Assert.Equal(new[] { "Product must be active" }, outcome.Messages);
    }
}
