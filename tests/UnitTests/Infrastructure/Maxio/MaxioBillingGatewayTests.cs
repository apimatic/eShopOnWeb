using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioBillingGatewayTests
{
    private static readonly Uri SiteBase = new("https://cp-exp-4.chargify.com/");

    [Fact]
    public async Task GetFamilyPlansAsyncParsesProductWrappersAndSkipsArchived()
    {
        var handler = new StubHttpMessageHandler(request =>
            Json(HttpStatusCode.OK, """
            [
              {"product":{"id":7126957,"handle":"eshop-pro","name":"Pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"taxable":false,"archived_at":null}},
              {"product":{"id":7126958,"handle":"basic-plan","name":"Basic","price_in_cents":2900,"interval":1,"interval_unit":"month","require_credit_card":false,"taxable":false,"archived_at":null}},
              {"product":{"id":7126959,"handle":"retired","name":"Retired","price_in_cents":100,"interval":1,"interval_unit":"month","require_credit_card":false,"taxable":false,"archived_at":"2030-01-01T00:00:00-05:00"}}
            ]
            """));

        var plans = await Gateway(handler).GetFamilyPlansAsync("eshop-subscribe");

        Assert.Equal(2, plans.Count);
        Assert.Equal("eshop-pro", plans[0].Handle);
        Assert.Equal(29900, plans[0].PriceInCents);
        Assert.False(plans[0].RequiresPaymentMethod);

        // Plan handles are looked up by handle: form on the family's products endpoint.
        Assert.Equal(HttpMethod.Get, handler.Requests.Single().Method);
        Assert.Equal("/product_families/handle:eshop-subscribe/products.json", handler.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task FindCustomerByReferenceAsyncReturnsNullOnNotFound()
    {
        var handler = new StubHttpMessageHandler(request =>
            new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("Resource Not Found") });

        var customer = await Gateway(handler).FindCustomerByReferenceAsync("eshop-user-abc");

        Assert.Null(customer);
    }

    [Fact]
    public async Task FindCustomerByReferenceAsyncUnwrapsCustomer()
    {
        var handler = new StubHttpMessageHandler(request =>
            Json(HttpStatusCode.OK, """
            {"customer":{"id":3023075,"reference":"eshop-user-abc","first_name":"demouser","last_name":"Shopper","email":"demouser@microsoft.com"}}
            """));

        var customer = await Gateway(handler).FindCustomerByReferenceAsync("eshop-user-abc");

        Assert.NotNull(customer);
        Assert.Equal(3023075, customer!.Id);
        Assert.Equal("eshop-user-abc", customer.Reference);
    }

    [Fact]
    public async Task CreateSubscriptionAsyncPostsSnakeCaseBodyWithUniquenessToken()
    {
        var handler = new StubHttpMessageHandler(request =>
            Json(HttpStatusCode.Created, """
            {"subscription":{"id":7126999,"state":"active","reference":"eshop-sub-abc-eshop-pro","product_price_in_cents":29900,"currency":"USD","current_period_ends_at":"2030-02-05T14:48:10-05:00","activated_at":"2030-01-06T14:48:10-05:00","created_at":"2030-01-06T14:48:10-05:00","product":{"id":7126957,"handle":"eshop-pro","name":"eShop Webinars Pro"}}}
            """));

        var subscription = await Gateway(handler).CreateSubscriptionAsync(new CreateMaxioSubscriptionRequest
        {
            CustomerId = 3023075,
            ProductId = 7126957,
            Reference = "eshop-sub-abc-eshop-pro",
            UniquenessToken = "unique-token-1",
            PaymentCollectionMethod = "remittance",
        });

        Assert.Equal(7126999, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.ProductHandle);
        Assert.Equal(29900, subscription.PriceInCents);
        Assert.Equal("USD", subscription.Currency);
        Assert.NotNull(subscription.NextBillingAt);

        var sentBody = handler.Bodies.Single();
        Assert.Contains("\"customer_id\":3023075", sentBody);
        Assert.Contains("\"product_id\":7126957", sentBody);
        Assert.Contains("\"reference\":\"eshop-sub-abc-eshop-pro\"", sentBody);
        Assert.Contains("\"uniqueness_token\":\"unique-token-1\"", sentBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", sentBody);
    }

    [Fact]
    public async Task CreateSubscriptionAsyncOmitsPaymentCollectionMethodWhenNotSet()
    {
        var handler = new StubHttpMessageHandler(request =>
            Json(HttpStatusCode.Created, """
            {"subscription":{"id":7126999,"state":"active","product":{"id":7126957,"handle":"eshop-pro","name":"Pro"},"created_at":"2030-01-06T14:48:10-05:00"}}
            """));

        await Gateway(handler).CreateSubscriptionAsync(new CreateMaxioSubscriptionRequest
        {
            CustomerId = 3023075,
            ProductId = 7126957,
            Reference = "eshop-sub-abc-eshop-pro",
            UniquenessToken = "unique-token-1",
        });

        Assert.DoesNotContain("payment_collection_method", handler.Bodies.Single());
    }

    [Fact]
    public async Task NonSuccessResponsesSurfaceParsedValidationErrors()
    {
        var handler = new StubHttpMessageHandler(request =>
            Json(HttpStatusCode.UnprocessableEntity, """
            {"errors":["Plan handle: does not exist"]}
            """));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            Gateway(handler).CreateSubscriptionAsync(new CreateMaxioSubscriptionRequest
            {
                CustomerId = 1,
                ProductId = 2,
                Reference = "r",
                UniquenessToken = "t",
            }));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("Plan handle: does not exist", exception.Errors);
    }

    [Fact]
    public async Task GetCustomerSubscriptionsAsyncParsesSubscriptionArray()
    {
        var handler = new StubHttpMessageHandler(request =>
            Json(HttpStatusCode.OK, """
            [
              {"subscription":{"id":1,"state":"active","product":{"id":7,"handle":"h","name":"n"},"product_price_in_cents":100,"currency":"USD","current_period_ends_at":"2030-02-05T00:00:00-05:00","created_at":"2030-01-05T00:00:00-05:00"}},
              {"subscription":{"id":2,"state":"canceled","product":{"id":8,"handle":"h2","name":"n2"},"product_price_in_cents":200,"currency":"USD","created_at":"2030-01-05T00:00:00-05:00"}}
            ]
            """));

        var subscriptions = await Gateway(handler).GetCustomerSubscriptionsAsync(3023075);

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal("canceled", subscriptions[1].State);
        Assert.True(subscriptions[1].IsTerminated);
        Assert.False(subscriptions[0].IsTerminated);
    }

    private static MaxioBillingGateway Gateway(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = SiteBase });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync());
            }

            return _responder(request);
        }
    }
}
