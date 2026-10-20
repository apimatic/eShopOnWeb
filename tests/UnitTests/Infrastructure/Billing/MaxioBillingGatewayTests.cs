using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> Bodies { get; } = new();

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? null : request.Content.ReadAsStringAsync(ct).Result);
        var response = _responder(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

public class MaxioBillingGatewayTests
{
    private const string Family = "fam-under-test";
    private const string SubRef = "eshop-sub:u@x.test:eshop-pro";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string SubscriptionJson(int id = 5, string reference = SubRef) =>
        "{\"subscription\":{\"id\":" + id + ",\"state\":\"active\",\"product_price_in_cents\":29900,\"currency\":\"USD\",\"reference\":\"" + reference + "\",\"next_assessment_at\":\"2030-01-15T00:00:00Z\",\"product\":{\"handle\":\"eshop-pro\",\"name\":\"Pro\",\"price_in_cents\":29900}}}";

    private static MaxioBillingGateway NewGateway(StubHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "unit-test-key", Password = "x" },
            Retry = RetryOptions.Disabled(),
        };
        options.Server.Production.Us.BaseUrl = "http://maxio.test";
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = new MaxioSettings { ApiKey = "unit-test-key", Subdomain = "unit", ProductFamilyHandle = Family };
        return new MaxioBillingGateway(client, settings, NullLogger<MaxioBillingGateway>.Instance);
    }

    [Fact]
    public async Task ListPlansAddressesTheFamilyByHandleAndSkipsArchivedPlans()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            "[{\"product\":{\"id\":1,\"handle\":\"eshop-pro\",\"name\":\"Pro\",\"price_in_cents\":29900,\"interval\":1,\"interval_unit\":\"month\"}},{\"product\":{\"id\":2,\"handle\":\"old\",\"archived_at\":\"2020-01-01T00:00:00Z\"}}]"));

        var result = await NewGateway(handler).ListPlansAsync(default);

        var plan = Assert.Single(result.Plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal("month", plan.IntervalUnit);
        Assert.False(result.IsTruncated);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("handle", Uri.UnescapeDataString(request.RequestUri!.AbsolutePath));
        Assert.Contains(Family, Uri.UnescapeDataString(request.RequestUri.AbsolutePath));
        Assert.Contains("per_page=200", request.RequestUri.Query);
        Assert.StartsWith("Basic ", request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task ListPlansReportsTruncationAtThePageCap()
    {
        var fullPage = "[" + string.Join(",", Enumerable.Range(0, 200).Select(i => "{\"product\":{\"handle\":\"p" + i + "\"}}")) + "]";
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, fullPage));

        var result = await NewGateway(handler).ListPlansAsync(default);

        Assert.True(result.IsTruncated);
        Assert.Equal(10, handler.Requests.Count);
    }

    [Fact]
    public async Task FindCustomerReturnsNullOn404ButNotOtherErrors()
    {
        var notFound = new StubHandler(_ => Json(HttpStatusCode.NotFound, "{}"));
        Assert.Null(await NewGateway(notFound).FindCustomerByReferenceAsync("r", default));

        var unauthorized = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, "{}"));
        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => NewGateway(unauthorized).FindCustomerByReferenceAsync("r", default));
        Assert.Equal(BillingFailureKind.ConfigFault, ex.Kind);
    }

    [Fact]
    public async Task FindSubscriptionReturnsNullOn404AndMapsFields()
    {
        var missing = new StubHandler(_ => Json(HttpStatusCode.NotFound, "{}"));
        Assert.Null(await NewGateway(missing).FindSubscriptionByReferenceAsync(SubRef, default));

        var found = new StubHandler(_ => Json(HttpStatusCode.OK, SubscriptionJson()));
        var info = await NewGateway(found).FindSubscriptionByReferenceAsync(SubRef, default);
        Assert.Equal(5, info!.SubscriptionId);
        Assert.Equal("eshop-pro", info.PlanHandle);
        Assert.Equal(29900, info.PriceInCents);
        Assert.Equal("active", info.State);
        Assert.Equal(DateTimeOffset.Parse("2030-01-15T00:00:00Z"), info.NextBillingDate);
    }

    [Fact]
    public async Task CreateSubscriptionSendsHandleCustomerAndReferenceOnce()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Created, SubscriptionJson()));

        var info = await NewGateway(handler).CreateSubscriptionAsync(7, "eshop-pro", SubRef, default);

        Assert.Equal(5, info.SubscriptionId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        var body = handler.Bodies[0]!;
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":7", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        Assert.Contains($"\"reference\":\"{SubRef}\"", body);
    }

    [Fact]
    public async Task CreateSubscriptionConnectionFailureIsSettledByReReadingTheReference()
    {
        var handler = new StubHandler(r => r.Method == HttpMethod.Post
            ? throw new HttpRequestException("connection reset")
            : Json(HttpStatusCode.OK, SubscriptionJson()));

        var info = await NewGateway(handler).CreateSubscriptionAsync(7, "eshop-pro", SubRef, default);

        Assert.Equal(5, info.SubscriptionId);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post)); // never re-sent
        Assert.Contains(handler.Requests, r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.Contains("lookup"));
    }

    [Fact]
    public async Task CreateSubscriptionConnectionFailureWithNothingAtTheProviderIsADefiniteFailure()
    {
        var handler = new StubHandler(r => r.Method == HttpMethod.Post
            ? throw new HttpRequestException("connection reset")
            : r.RequestUri!.AbsolutePath.Contains("lookup")
                ? Json(HttpStatusCode.NotFound, "{}")
                : Json(HttpStatusCode.OK, "[]"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() =>
            NewGateway(handler).CreateSubscriptionAsync(7, "eshop-pro", SubRef, default));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task CreateSubscriptionConnectionFailureWhenReReadAlsoFailsIsOutcomeUnknown()
    {
        var handler = new StubHandler(r => r.Method == HttpMethod.Post
            ? throw new HttpRequestException("connection reset")
            : Json(HttpStatusCode.ServiceUnavailable, "{}"));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() =>
            NewGateway(handler).CreateSubscriptionAsync(7, "eshop-pro", SubRef, default));

        Assert.Equal(BillingFailureKind.OutcomeUnknown, ex.Kind);
    }

    [Fact]
    public async Task CreateSubscription422IsADefiniteRejection()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Product is not available"]}"""));

        await Assert.ThrowsAsync<BillingProviderRejectedException>(() =>
            NewGateway(handler).CreateSubscriptionAsync(7, "eshop-pro", SubRef, default));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CreateSubscriptionReadableErrorWithUnexpectedBodyStillMapsByStatus()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.UnprocessableEntity, """{"errors":"not-a-list"}"""));

        await Assert.ThrowsAsync<BillingProviderRejectedException>(() =>
            NewGateway(handler).CreateSubscriptionAsync(7, "eshop-pro", SubRef, default));
    }

    [Fact]
    public async Task CreateCustomerConnectionFailureIsSettledByReference()
    {
        var handler = new StubHandler(r => r.Method == HttpMethod.Post
            ? throw new HttpRequestException("reset")
            : Json(HttpStatusCode.OK, """{"customer":{"id":9,"reference":"eshop-user:u@x.test"}}"""));

        var customer = await NewGateway(handler)
            .CreateCustomerAsync(new NewBillingCustomer("eshop-user:u@x.test", "u@x.test", "u", "Shopper"), default);

        Assert.Equal(9, customer.Id);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post));
    }

    [Fact]
    public async Task ReadsMapProviderServerErrorsToUnavailable()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.BadGateway, "{}"));
        var gateway = NewGateway(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() => gateway.ListPlansAsync(default));

        Assert.Equal(BillingFailureKind.Unavailable, ex.Kind);
    }

    [Fact]
    public async Task UnreadableSuccessBodyOnReadIsAConfigFaultNotAnAbsence()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{"customer":"nope"}"""));

        var ex = await Assert.ThrowsAsync<BillingProviderException>(() =>
            NewGateway(handler).FindCustomerByReferenceAsync("r", default));

        Assert.Equal(BillingFailureKind.ConfigFault, ex.Kind);
    }

    [Fact]
    public async Task ListCustomerSubscriptionsSkipsMalformedElements()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, "[" + SubscriptionJson() + ",{\"subscription\":null}]"));

        var list = await NewGateway(handler).ListCustomerSubscriptionsAsync(7, default);

        Assert.Single(list);
    }
}
