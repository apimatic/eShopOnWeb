using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioAdvancedBillingClientTests
{
    [Fact]
    public async Task ListsProductsForFamilyUsingHandlePrefixedPath()
    {
        var json = """
            [
              {
                "product": {
                  "id": 7126957,
                  "name": "Pro Plan",
                  "handle": "eshop-pro",
                  "description": "Monthly pro",
                  "price_in_cents": 29900,
                  "interval": 1,
                  "interval_unit": "month",
                  "require_credit_card": false,
                  "archived_at": null,
                  "product_family": { "id": 1, "handle": "eshop-subscribe", "name": "eShop" }
                }
              }
            ]
            """;
        var (client, handler) = CreateClient(json);

        var plans = await client.ListProductsForFamilyAsync("eshop-subscribe");

        Assert.Equal("/product_families/handle%3Aeshop-subscribe/products.json?page=1&per_page=200", handler.LastRequest!.RequestUri!.PathAndQuery);
        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299m, plan.Price);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.False(plan.RequireCreditCard);
    }

    [Fact]
    public async Task LooksUpCustomerByReferenceAndReturnsNullOn404()
    {
        var (client, handler) = CreateClient("{}", HttpStatusCode.NotFound);

        var customer = await client.FindCustomerByReferenceAsync("user-1");

        Assert.Null(customer);
        Assert.Equal("/customers/lookup.json?reference=user-1", handler.LastRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task CreatesSubscriptionWithRemittanceCollectionMethod()
    {
        var json = """
            {
              "subscription": {
                "id": 55,
                "state": "active",
                "product_price_in_cents": 2900,
                "current_period_ends_at": "2026-09-19T12:00:00-04:00",
                "next_assessment_at": "2026-09-19T12:00:00-04:00",
                "reference": "user-1:basic-plan",
                "product": { "id": 2, "handle": "basic-plan", "name": "Basic Plan", "price_in_cents": 2900 }
              }
            }
            """;
        var (client, handler) = CreateClient(json, HttpStatusCode.Created);

        var created = await client.CreateSubscriptionAsync(new CreateBillingSubscription
        {
            ProductHandle = "basic-plan",
            CustomerId = 9,
            Reference = "user-1:basic-plan"
        });

        Assert.Equal(55, created.Id);
        Assert.Equal("active", created.State);
        Assert.Equal(29m, created.Price);
        Assert.NotNull(created.NextBillingDate);
        Assert.Contains("\"product_handle\":\"basic-plan\"", handler.LastRequestBody);
        Assert.Contains("\"customer_id\":9", handler.LastRequestBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", handler.LastRequestBody);
        Assert.Equal("/subscriptions.json", handler.LastRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public void ParsesMaxioErrorArrays()
    {
        var errors = MaxioAdvancedBillingClient.ParseErrors("""{"errors":["Name: cannot be blank."]}""");
        Assert.Equal(new[] { "Name: cannot be blank." }, errors);
    }

    private static (MaxioAdvancedBillingClient Client, StubHandler Handler) CreateClient(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler
        {
            Response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            }
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://acme.chargify.com/") };
        return (new MaxioAdvancedBillingClient(http, NullLogger<MaxioAdvancedBillingClient>.Instance), handler);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);
        public HttpRequestMessage? LastRequest { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return Response;
        }
    }
}
