using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionBillingServiceTests;

public class MaxioOptionsTests
{
    [Fact]
    public void ResolveBaseUrl_UsesExplicitOverride()
    {
        var options = new MaxioOptions
        {
            Subdomain = "ignored-site",
            BaseUrl = "https://billing.example.test/v1"
        };

        Assert.Equal("https://billing.example.test/v1/", options.ResolveBaseUrl());
    }

    [Fact]
    public void ResolveBaseUrl_DerivesChargifyHostFromSubdomain()
    {
        var options = new MaxioOptions { Subdomain = "acme" };

        Assert.Equal("https://acme.chargify.com/", options.ResolveBaseUrl());
    }
}

public class MaxioSubscriptionBillingServiceTests
{
    private readonly IMaxioAdvancedBillingClient _maxio = Substitute.For<IMaxioAdvancedBillingClient>();
    private readonly ILogger<MaxioSubscriptionBillingService> _logger = Substitute.For<ILogger<MaxioSubscriptionBillingService>>();
    private readonly MaxioOptions _options = new()
    {
        ApiKey = "test-key",
        Subdomain = "acme",
        ProductFamilyHandle = "eshop-subscribe"
    };

    private readonly Shopper _shopper = new("user-1", "demouser@microsoft.com", "demouser@microsoft.com");

    private MaxioSubscriptionBillingService CreateSut() =>
        new(_maxio, Options.Create(_options), _logger);

    [Fact]
    public async Task ListAvailablePlans_MapsPriceAndSkipsArchived()
    {
        _maxio.ListProductsForFamilyAsync("eshop-subscribe", Arg.Any<CancellationToken>())
            .Returns(new List<MaxioProduct>
            {
                new()
                {
                    Id = 1,
                    Handle = "eshop-pro",
                    Name = "Pro Plan",
                    PriceInCents = 29900,
                    Interval = 1,
                    IntervalUnit = "month"
                },
                new()
                {
                    Id = 2,
                    Handle = "archived-plan",
                    Name = "Gone",
                    PriceInCents = 100,
                    ArchivedAt = DateTimeOffset.UtcNow
                }
            });

        var plans = await CreateSut().ListAvailablePlansAsync();

        var plan = Assert.Single(plans);
        Assert.Equal("eshop-pro", plan.Handle);
        Assert.Equal(299.00m, plan.Price);
        Assert.Equal(29900, plan.PriceInCents);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerOnce_AndReturnsExistingSubscriptionOnRepeat()
    {
        var product = ProPlan();
        var customer = new MaxioCustomer { Id = 42, Reference = _shopper.Id, Email = _shopper.Email };
        var subscription = LiveSubscription(product, customer);

        _maxio.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(product);
        _maxio.FindCustomerByReferenceAsync(_shopper.Id, Arg.Any<CancellationToken>())
            .Returns((MaxioCustomer?)null, customer);
        _maxio.CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>()).Returns(customer);
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null, subscription);
        _maxio.ListCustomerSubscriptionsAsync(42, Arg.Any<CancellationToken>())
            .Returns(new List<MaxioSubscription>(), new List<MaxioSubscription> { subscription });
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>()).Returns(subscription);

        var sut = CreateSut();
        var first = await sut.SubscribeAsync(_shopper, "eshop-pro");
        var second = await sut.SubscribeAsync(_shopper, "eshop-pro");

        Assert.Equal(9001, first.Id);
        Assert.False(first.AlreadyExisted);
        Assert.Equal("active", first.State);
        Assert.Equal("eshop-pro", first.ProductHandle);
        Assert.Equal(299.00m, first.Price);
        Assert.Equal(subscription.NextAssessmentAt, first.NextBillingAt);

        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.Id, second.Id);

        await _maxio.Received(1).CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
        await _maxio.Received(1).CreateSubscriptionAsync(Arg.Is<MaxioCreateSubscription>(s =>
            s.CustomerId == 42 && s.ProductHandle == "eshop-pro" && s.Reference == "user-1:eshop-pro" && s.PaymentCollectionMethod == "remittance"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_ReusesCustomerWhenLookupFindsExisting()
    {
        var product = ProPlan();
        var customer = new MaxioCustomer { Id = 7, Reference = _shopper.Id };
        _maxio.GetProductByHandleAsync("eshop-pro", Arg.Any<CancellationToken>()).Returns(product);
        _maxio.FindCustomerByReferenceAsync(_shopper.Id, Arg.Any<CancellationToken>()).Returns(customer);
        _maxio.FindSubscriptionByReferenceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((MaxioSubscription?)null);
        _maxio.ListCustomerSubscriptionsAsync(7, Arg.Any<CancellationToken>()).Returns(new List<MaxioSubscription>());
        _maxio.CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>())
            .Returns(LiveSubscription(product, customer));

        await CreateSut().SubscribeAsync(_shopper, "eshop-pro");

        await _maxio.DidNotReceive().CreateCustomerAsync(Arg.Any<MaxioCreateCustomer>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_Throws()
    {
        _maxio.GetProductByHandleAsync("nope", Arg.Any<CancellationToken>()).Returns((MaxioProduct?)null);

        var ex = await Assert.ThrowsAsync<UnknownSubscriptionPlanException>(
            () => CreateSut().SubscribeAsync(_shopper, "nope"));

        Assert.Equal(400, ex.StatusCode);
        await _maxio.DidNotReceive().CreateSubscriptionAsync(Arg.Any<MaxioCreateSubscription>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListMySubscriptions_ReturnsEmptyWhenCustomerDoesNotExist()
    {
        _maxio.FindCustomerByReferenceAsync(_shopper.Id, Arg.Any<CancellationToken>()).Returns((MaxioCustomer?)null);

        var result = await CreateSut().ListMySubscriptionsAsync(_shopper);

        Assert.Empty(result);
        await _maxio.DidNotReceive().ListCustomerSubscriptionsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SplitName_UsesEmailLocalPart()
    {
        var (first, last) = MaxioSubscriptionBillingService.SplitName(_shopper);
        Assert.Equal("Demouser", first);
        Assert.Equal("Customer", last);
    }

    private static MaxioProduct ProPlan() => new()
    {
        Id = 10,
        Handle = "eshop-pro",
        Name = "Pro Plan",
        PriceInCents = 29900,
        Interval = 1,
        IntervalUnit = "month",
        ProductFamily = new MaxioProductFamily { Handle = "eshop-subscribe" }
    };

    private static MaxioSubscription LiveSubscription(MaxioProduct product, MaxioCustomer customer) => new()
    {
        Id = 9001,
        State = "active",
        ProductPriceInCents = 29900,
        NextAssessmentAt = DateTimeOffset.Parse("2026-09-19T12:00:00Z"),
        Product = product,
        Customer = customer,
        Reference = "user-1:eshop-pro"
    };
}

public class MaxioAdvancedBillingClientTests
{
    [Fact]
    public async Task ListProductsForFamily_UsesHandlePrefixedPath()
    {
        var handler = new ScriptedHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/product_families/handle:eshop-subscribe/products.json", request.RequestUri!.AbsolutePath);
            Assert.Contains("include_archived=false", request.RequestUri.Query);
            return Json(HttpStatusCode.OK, """[{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""");
        });

        var client = CreateClient(handler);
        var products = await client.ListProductsForFamilyAsync("eshop-subscribe");

        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
    }

    [Fact]
    public async Task FindCustomerByReference_ReturnsNullOn404()
    {
        var handler = new ScriptedHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = CreateClient(handler);

        var customer = await client.FindCustomerByReferenceAsync("user-1");

        Assert.Null(customer);
    }

    [Fact]
    public async Task CreateSubscription_PostsProductHandleAndCustomerId()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new ScriptedHandler((request, _) =>
        {
            captured = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.Created, """{"subscription":{"id":77,"state":"active","product_price_in_cents":2900,"product":{"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}}}""");
        });

        var client = CreateClient(handler);
        var created = await client.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            CustomerId = 42,
            ProductHandle = "basic-plan",
            Reference = "user-1:basic-plan"
        });

        Assert.Equal(77, created.Id);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/subscriptions.json", captured.RequestUri!.AbsolutePath);
        Assert.Contains("\"product_handle\":\"basic-plan\"", body);
        Assert.Contains("\"customer_id\":42", body);
        Assert.Contains("\"reference\":\"user-1:basic-plan\"", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
    }

    private static MaxioAdvancedBillingClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://acme.chargify.com/")
        };
        return new MaxioAdvancedBillingClient(http, Substitute.For<ILogger<MaxioAdvancedBillingClient>>());
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

        public ScriptedHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request, cancellationToken));
    }
}
