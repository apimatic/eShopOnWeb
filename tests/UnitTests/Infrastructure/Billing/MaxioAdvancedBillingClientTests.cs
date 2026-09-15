using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioAdvancedBillingClientTests
{
    [Fact]
    public async Task ListProductsForProductFamilyAsync_UsesHandlePrefixedPathAndMapsBody()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler((request, _) =>
        {
            captured = request;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    [
                      {
                        "product": {
                          "id": 1,
                          "name": "Pro Plan",
                          "handle": "eshop-pro",
                          "description": "Pro",
                          "price_in_cents": 29900,
                          "interval": 1,
                          "interval_unit": "month",
                          "product_family": { "handle": "eshop-subscribe" }
                        }
                      }
                    ]
                    """)
            };
            return Task.FromResult(response);
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.chargify.com/") };
        var client = new MaxioAdvancedBillingClient(
            httpClient,
            new StaticOptionsMonitor(new MaxioOptions
            {
                ApiKey = "secret-key",
                Subdomain = "example",
                ProductFamilyHandle = "eshop-subscribe"
            }),
            NullLogger<MaxioAdvancedBillingClient>.Instance);

        var products = await client.ListProductsForProductFamilyAsync("eshop-subscribe");

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Get, captured!.Method);
        Assert.Equal("/product_families/handle:eshop-subscribe/products.json?page=1&per_page=200", captured.RequestUri!.PathAndQuery);
        Assert.Equal("Basic", captured.Headers.Authorization!.Scheme);
        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("eshop-subscribe", product.ProductFamilyHandle);
    }

    [Fact]
    public async Task ReadCustomerByReferenceAsync_ReturnsNullOn404()
    {
        var handler = new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.chargify.com/") };
        var client = new MaxioAdvancedBillingClient(
            httpClient,
            new StaticOptionsMonitor(new MaxioOptions
            {
                ApiKey = "secret-key",
                Subdomain = "example",
                ProductFamilyHandle = "eshop-subscribe"
            }),
            NullLogger<MaxioAdvancedBillingClient>.Instance);

        var customer = await client.ReadCustomerByReferenceAsync("missing");

        Assert.Null(customer);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<MaxioOptions>
    {
        public StaticOptionsMonitor(MaxioOptions currentValue) => CurrentValue = currentValue;
        public MaxioOptions CurrentValue { get; }
        public MaxioOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<MaxioOptions, string?> listener) => null;
    }
}
