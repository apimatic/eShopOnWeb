using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioOptionsTests
{
    [Fact]
    public void GetApiBaseUrl_UsesSubdomainWhenBaseUrlIsEmpty()
    {
        var options = new MaxioOptions { Subdomain = "cp-exp-2" };
        Assert.Equal("https://cp-exp-2.chargify.com/", options.GetApiBaseUrl());
    }

    [Fact]
    public void GetApiBaseUrl_UsesBaseUrlVerbatimWhenSet()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored",
            BaseUrl = "https://custom.example.com/v1"
        };
        Assert.Equal("https://custom.example.com/v1/", options.GetApiBaseUrl());
    }

    [Fact]
    public void IsConfigured_RequiresKeyFamilyAndSubdomainOrBaseUrl()
    {
        Assert.False(new MaxioOptions().IsConfigured);
        Assert.True(new MaxioOptions
        {
            ApiKey = "k",
            ProductFamilyHandle = "fam",
            Subdomain = "site"
        }.IsConfigured);
        Assert.True(new MaxioOptions
        {
            ApiKey = "k",
            ProductFamilyHandle = "fam",
            BaseUrl = "https://example.com"
        }.IsConfigured);
    }
}

public class MaxioBillingClientTests
{
    [Fact]
    public async Task ListProductsForProductFamily_DeserializesSpecEnvelope()
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
                  "trial_price_in_cents": null,
                  "require_credit_card": false,
                  "archived_at": null,
                  "product_family": { "id": 1, "handle": "eshop-subscribe", "name": "eShop" }
                }
              }
            ]
            """;

        HttpRequestMessage? captured = null;
        var client = CreateClient(req =>
        {
            captured = req;
            return Json(json);
        });

        var plans = await client.ListProductsForProductFamilyAsync();

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Contains("product_families/handle:eshop-subscribe/products.json", captured.RequestUri!.ToString());

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(29900, plan.PriceInCents);
        Assert.Equal(299m, plan.Price);
        Assert.False(plan.RequireCreditCard);
    }

    [Fact]
    public async Task ReadCustomerByReference_ReturnsNullOn404()
    {
        var client = CreateClient(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        var customer = await client.ReadCustomerByReferenceAsync("demouser@microsoft.com");
        Assert.Null(customer);
    }

    [Fact]
    public async Task CreateSubscription_PostsSpecShape()
    {
        string? body = null;
        var client = CreateClient(req =>
        {
            body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""
                {
                  "subscription": {
                    "id": 55,
                    "state": "active",
                    "product_price_in_cents": 29900,
                    "next_assessment_at": "2026-09-19T00:00:00-04:00",
                    "current_period_ends_at": "2026-09-19T00:00:00-04:00",
                    "product": { "id": 1, "name": "Pro Plan", "handle": "eshop-pro", "price_in_cents": 29900, "interval": 1, "interval_unit": "month" },
                    "customer": { "id": 9, "email": "a@b.c", "first_name": "a", "last_name": "b" }
                  }
                }
                """);
        });

        var created = await client.CreateSubscriptionAsync(9, "eshop-pro", "ref-1");

        Assert.Equal(55, created.Id);
        Assert.Equal("active", created.State);
        Assert.Equal("eshop-pro", created.ProductHandle);
        Assert.NotNull(created.NextBillingDate);

        Assert.NotNull(body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":9", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
        Assert.Contains("\"reference\":\"ref-1\"", body);
    }

    private static MaxioBillingClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var http = new HttpClient(new StubHandler(responder))
        {
            BaseAddress = new Uri("https://example.chargify.com/")
        };
        var options = new StaticOptions(new MaxioOptions
        {
            ApiKey = "key",
            Subdomain = "example",
            ProductFamilyHandle = "eshop-subscribe"
        });
        return new MaxioBillingClient(http, options, Substitute.For<IAppLogger<MaxioBillingClient>>());
    }

    private static HttpResponseMessage Json(string json) =>
        new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private sealed class StaticOptions : IOptions<MaxioOptions>
    {
        public StaticOptions(MaxioOptions value) => Value = value;
        public MaxioOptions Value { get; }
    }
}
