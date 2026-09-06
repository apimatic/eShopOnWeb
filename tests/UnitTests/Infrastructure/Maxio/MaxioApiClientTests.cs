using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Verifies that the client sends exactly what maxio-spec/openapi.yaml specifies: the documented
/// paths and query parameters, the BasicAuth scheme, and the request body wrapper shapes.
/// </summary>
public class MaxioApiClientTests
{
    private const string ApiKey = "test-api-key";

    private readonly StubHttpMessageHandler _handler = new();

    private MaxioApiClient CreateClient(MaxioSettings? settings = null)
    {
        settings ??= new MaxioSettings { ApiKey = ApiKey, Subdomain = "acme", ProductFamilyHandle = "eshop-subscribe" };
        var monitor = new StaticOptionsMonitor<MaxioSettings>(settings);

        var authenticated = new MaxioAuthenticationHandler(monitor) { InnerHandler = _handler };
        var httpClient = new HttpClient(authenticated)
        {
            BaseAddress = settings.ResolveBaseAddress(),
            Timeout = Timeout.InfiniteTimeSpan
        };

        return new MaxioApiClient(httpClient, monitor, NullLogger<MaxioApiClient>.Instance);
    }

    [Fact]
    public async Task SendsTheApiKeyAsBasicAuthWithThePasswordX()
    {
        _handler.Respond(HttpStatusCode.OK, """{"site":{"id":1,"currency":"USD"}}""");

        await CreateClient().ReadSiteAsync();

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("Basic", request.AuthScheme);
        Assert.Equal($"{ApiKey}:x", Encoding.UTF8.GetString(Convert.FromBase64String(request.AuthParameter!)));
    }

    [Fact]
    public async Task DerivesTheBaseAddressFromTheSubdomain()
    {
        _handler.Respond(HttpStatusCode.OK, """{"site":{"id":1}}""");

        await CreateClient().ReadSiteAsync();

        Assert.Equal("https://acme.chargify.com/site.json", _handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task PrefersAnExplicitBaseUrlOverTheSubdomain()
    {
        _handler.Respond(HttpStatusCode.OK, """{"site":{"id":1}}""");

        var settings = new MaxioSettings
        {
            ApiKey = ApiKey,
            Subdomain = "acme",
            ProductFamilyHandle = "eshop-subscribe",
            BaseUrl = "https://billing.internal.example.com/maxio"
        };

        await CreateClient(settings).ReadSiteAsync();

        Assert.Equal("https://billing.internal.example.com/maxio/site.json", _handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task ReadsTheSiteCurrencyAndInvoicingArchitecture()
    {
        _handler.Respond(HttpStatusCode.OK,
            """{"site":{"id":93060,"name":"CP","subdomain":"acme","currency":"USD","relationship_invoicing_enabled":true,"default_payment_collection_method":"automatic","test":true}}""");

        var site = await CreateClient().ReadSiteAsync();

        Assert.Equal("USD", site.Currency);
        Assert.True(site.RelationshipInvoicingEnabled);
        Assert.Equal("automatic", site.DefaultPaymentCollectionMethod);
        Assert.True(site.Test);
    }

    [Fact]
    public async Task AddressesTheProductFamilyByHandle()
    {
        _handler.Respond(HttpStatusCode.OK, "[]");

        await CreateClient().ListProductsForProductFamilyAsync("eshop-subscribe");

        var uri = _handler.Requests[0].Uri;
        Assert.Equal("/product_families/handle:eshop-subscribe/products.json", uri.AbsolutePath);
        Assert.Contains("per_page=200", uri.Query);
        Assert.Contains("page=1", uri.Query);
    }

    [Fact]
    public async Task UnwrapsTheProductEnvelopeFromTheProductList()
    {
        _handler.Respond(HttpStatusCode.OK,
            """[{"product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"product_family":{"id":9,"handle":"eshop-subscribe","name":"eShopSubscribe"}}}]""");

        var products = await CreateClient().ListProductsForProductFamilyAsync("eshop-subscribe");

        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("month", product.IntervalUnit);
        Assert.False(product.RequireCreditCard);
        Assert.Equal("eshop-subscribe", product.ProductFamily!.Handle);
    }

    [Fact]
    public async Task StopsPagingWhenAPageIsNotFull()
    {
        _handler.Respond(HttpStatusCode.OK, """[{"product":{"id":1,"handle":"a"}}]""");

        await CreateClient().ListProductsForProductFamilyAsync("eshop-subscribe");

        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task LooksUpACustomerByReference()
    {
        _handler.Respond(HttpStatusCode.OK, """{"customer":{"id":7,"reference":"eshoponweb-a@b.com","email":"a@b.com"}}""");

        var customer = await CreateClient().ReadCustomerByReferenceAsync("eshoponweb-a@b.com");

        Assert.Equal("/customers/lookup.json", _handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal("?reference=eshoponweb-a%40b.com", _handler.Requests[0].Uri.Query);
        Assert.Equal(7, customer!.Id);
    }

    [Fact]
    public async Task ReturnsNullWhenACustomerLookupIsNotFound()
    {
        _handler.Respond(HttpStatusCode.NotFound, "");

        var customer = await CreateClient().ReadCustomerByReferenceAsync("missing");

        Assert.Null(customer);
    }

    [Fact]
    public async Task ReturnsNullWhenASubscriptionLookupIsNotFound()
    {
        _handler.Respond(HttpStatusCode.NotFound, "");

        var subscription = await CreateClient().FindSubscriptionAsync("missing");

        Assert.Null(subscription);
    }

    [Fact]
    public async Task WrapsTheCustomerCreateBodyAsTheSpecificationRequires()
    {
        _handler.Respond(HttpStatusCode.Created, """{"customer":{"id":7}}""");

        await CreateClient().CreateCustomerAsync(new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = "Ada",
                LastName = "Lovelace",
                Email = "ada@example.com",
                Reference = "eshoponweb-ada@example.com"
            }
        });

        var request = _handler.Requests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/customers.json", request.Uri.AbsolutePath);

        using var body = JsonDocument.Parse(request.Body!);
        var customer = body.RootElement.GetProperty("customer");
        Assert.Equal("Ada", customer.GetProperty("first_name").GetString());
        Assert.Equal("Lovelace", customer.GetProperty("last_name").GetString());
        Assert.Equal("ada@example.com", customer.GetProperty("email").GetString());
        Assert.Equal("eshoponweb-ada@example.com", customer.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task WrapsTheSubscriptionCreateBodyAsTheSpecificationRequires()
    {
        _handler.Respond(HttpStatusCode.Created, """{"subscription":{"id":42,"state":"active"}}""");

        await CreateClient().CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = "eshop-pro",
                CustomerId = 7,
                Reference = "eshoponweb-ada--eshop-pro--abc",
                PaymentCollectionMethod = "remittance"
            }
        });

        var request = _handler.Requests[0];
        Assert.Equal("/subscriptions.json", request.Uri.AbsolutePath);

        using var body = JsonDocument.Parse(request.Body!);
        var subscription = body.RootElement.GetProperty("subscription");
        Assert.Equal("eshop-pro", subscription.GetProperty("product_handle").GetString());
        Assert.Equal(7, subscription.GetProperty("customer_id").GetInt64());
        Assert.Equal("eshoponweb-ada--eshop-pro--abc", subscription.GetProperty("reference").GetString());
        Assert.Equal("remittance", subscription.GetProperty("payment_collection_method").GetString());
    }

    [Fact]
    public async Task OmitsTheCollectionMethodWhenItIsNotSet()
    {
        _handler.Respond(HttpStatusCode.Created, """{"subscription":{"id":42}}""");

        await CreateClient().CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription { ProductHandle = "eshop-pro", CustomerId = 7, Reference = "r" }
        });

        using var body = JsonDocument.Parse(_handler.Requests[0].Body!);
        Assert.False(body.RootElement.GetProperty("subscription").TryGetProperty("payment_collection_method", out _));
    }

    [Fact]
    public async Task ReadsEveryFieldTheSubscribeConfirmationNeeds()
    {
        _handler.Respond(HttpStatusCode.Created,
            """
            {"subscription":{"id":94211099,"state":"active","reference":"ref-1","balance_in_cents":29900,
            "current_period_started_at":"2026-09-06T19:11:58+05:00","current_period_ends_at":"2026-10-06T19:11:58+05:00",
            "next_assessment_at":"2026-10-06T19:11:58+05:00","activated_at":"2026-09-06T19:12:02+05:00",
            "product_price_in_cents":29900,"currency":"USD","payment_collection_method":"remittance",
            "product":{"id":1,"name":"Pro Plan","handle":"eshop-pro","interval":1,"interval_unit":"month"},
            "customer":{"id":98839436,"reference":"eshoponweb-a@b.com"}}}
            """);

        var subscription = await CreateClient().CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest());

        Assert.Equal(94211099, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal("eshop-pro", subscription.Product!.Handle);
        Assert.Equal(29900, subscription.ProductPriceInCents);
        Assert.Equal("USD", subscription.Currency);
        Assert.Equal(98839436, subscription.Customer!.Id);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 6, 19, 11, 58, TimeSpan.FromHours(5)),
            subscription.NextAssessmentAt);
    }

    [Fact]
    public async Task ReportsTheMessagesFromAnErrorListResponse()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference: must be unique - that value has been taken."]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient().CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.Equal("Reference: must be unique - that value has been taken.", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task ReportsTheMessagesFromACustomerErrorResponse()
    {
        _handler.Respond(HttpStatusCode.UnprocessableEntity, """{"errors":{"customer":"can't be blank"}}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient().CreateCustomerAsync(new MaxioCreateCustomerRequest()));

        Assert.Equal("customer: can't be blank", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task ReportsAPlainStringErrorBody()
    {
        _handler.Respond(HttpStatusCode.NotFound, "\"A valid product_family_id is required\"");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient().ListProductsForProductFamilyAsync("nope"));

        Assert.Equal("A valid product_family_id is required", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task ReportsANonJsonErrorBody()
    {
        _handler.Respond(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() => CreateClient().ReadSiteAsync());

        Assert.Equal("<html>bad gateway</html>", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task ReportsAResponseThatDoesNotMatchTheContract()
    {
        _handler.Respond(HttpStatusCode.OK, "not json at all", "application/json");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() => CreateClient().ReadSiteAsync());

        Assert.Contains("does not match the contract", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task ReportsAConnectionFailureAsATransportFault()
    {
        _handler.RespondWith(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<MaxioTransportException>(() => CreateClient().ReadSiteAsync());
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
