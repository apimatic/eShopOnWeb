using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Pins the client to the request shapes declared in maxio-spec/openapi.yaml: the paths, the
/// <c>handle:</c> product-family addressing, the request envelopes and the documented 404 behaviour.
/// </summary>
public class MaxioApiClientTests
{
    private readonly RecordingHandler _handler = new();

    private MaxioApiClient CreateClient()
    {
        var httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://example.chargify.com/") };
        return new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);
    }

    [Fact]
    public async Task AddressesTheProductFamilyByHandle()
    {
        _handler.RespondWith(HttpStatusCode.OK, "[]");

        await CreateClient().ListProductsForProductFamilyAsync("eshop-subscribe");

        Assert.Equal(HttpMethod.Get, _handler.LastRequest!.Method);
        Assert.Equal("/product_families/handle:eshop-subscribe/products.json",
            _handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ReadsProductsOutOfTheirEnvelopes()
    {
        _handler.RespondWith(HttpStatusCode.OK, """
            [{"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,
              "interval":1,"interval_unit":"month","require_credit_card":false,
              "product_family":{"id":3023074,"handle":"eshop-subscribe"}}}]
            """);

        var products = await CreateClient().ListProductsForProductFamilyAsync("eshop-subscribe");

        var product = Assert.Single(products);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("month", product.IntervalUnit);
        Assert.False(product.RequireCreditCard);
        Assert.Equal("eshop-subscribe", product.ProductFamily!.Handle);
    }

    [Fact]
    public async Task LooksCustomersUpByReference()
    {
        _handler.RespondWith(HttpStatusCode.OK,
            """{"customer":{"id":42,"reference":"eshop:demouser@microsoft.com","email":"demouser@microsoft.com"}}""");

        var customer = await CreateClient().ReadCustomerByReferenceAsync("eshop:demouser@microsoft.com");

        Assert.Equal("/customers/lookup.json", _handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("reference=eshop%3Ademouser%40microsoft.com", _handler.LastRequest.RequestUri.Query);
        Assert.Equal(42, customer!.Id);
    }

    [Fact]
    public async Task TreatsTheDocumentedNotFoundAsNoCustomer()
    {
        _handler.RespondWith(HttpStatusCode.NotFound, string.Empty);

        var customer = await CreateClient().ReadCustomerByReferenceAsync("eshop:nobody");

        Assert.Null(customer);
    }

    [Fact]
    public async Task TreatsTheDocumentedNotFoundAsNoSubscription()
    {
        _handler.RespondWith(HttpStatusCode.NotFound, string.Empty);

        var subscription = await CreateClient().FindSubscriptionAsync("eshop:nobody:eshop-pro");

        Assert.Null(subscription);
    }

    [Fact]
    public async Task SendsTheCustomerEnvelopeInSnakeCase()
    {
        _handler.RespondWith(HttpStatusCode.Created, """{"customer":{"id":42}}""");

        await CreateClient().CreateCustomerAsync(new MaxioCreateCustomerRequest
        {
            Customer = new MaxioCreateCustomer
            {
                FirstName = "Demouser",
                LastName = "Customer",
                Email = "demouser@microsoft.com",
                Reference = "eshop:demouser@microsoft.com"
            }
        });

        Assert.Equal("/customers.json", _handler.LastRequest!.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(_handler.LastRequestBody!);
        var customer = body.RootElement.GetProperty("customer");
        Assert.Equal("Demouser", customer.GetProperty("first_name").GetString());
        Assert.Equal("Customer", customer.GetProperty("last_name").GetString());
        Assert.Equal("eshop:demouser@microsoft.com", customer.GetProperty("reference").GetString());
    }

    [Fact]
    public async Task SendsTheSubscriptionEnvelopeInSnakeCase()
    {
        _handler.RespondWith(HttpStatusCode.Created, """{"subscription":{"id":900,"state":"active"}}""");

        await CreateClient().CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioCreateSubscription
            {
                ProductHandle = "eshop-pro",
                CustomerId = 42,
                Reference = "eshop:demouser@microsoft.com:eshop-pro",
                PaymentCollectionMethod = "remittance"
            }
        });

        Assert.Equal("/subscriptions.json", _handler.LastRequest!.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(_handler.LastRequestBody!);
        var subscription = body.RootElement.GetProperty("subscription");
        Assert.Equal("eshop-pro", subscription.GetProperty("product_handle").GetString());
        Assert.Equal(42, subscription.GetProperty("customer_id").GetInt64());
        Assert.Equal("remittance", subscription.GetProperty("payment_collection_method").GetString());
    }

    [Fact]
    public async Task ReadsSubscriptionTimestampsAndNestedProduct()
    {
        _handler.RespondWith(HttpStatusCode.OK, """
            {"subscription":{"id":900,"state":"active","reference":"eshop:demouser@microsoft.com:eshop-pro",
             "product_price_in_cents":29900,"currency":"USD","payment_collection_method":"remittance",
             "next_assessment_at":"2026-10-06T22:59:24+05:00","created_at":"2026-09-06T22:59:24+05:00",
             "trial_started_at":null,"product":{"handle":"eshop-pro","name":"Pro Plan","interval":1,"interval_unit":"month"}}}
            """);

        var subscription = await CreateClient().FindSubscriptionAsync("eshop:demouser@microsoft.com:eshop-pro");

        Assert.Equal("active", subscription!.State);
        Assert.Equal(29900, subscription.ProductPriceInCents);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 22, 59, 24, TimeSpan.FromHours(5)), subscription.NextAssessmentAt);
        Assert.Null(subscription.TrialStartedAt);
        Assert.Equal("eshop-pro", subscription.Product!.Handle);
    }

    [Fact]
    public async Task SurfacesTheErrorListMaxioReturns()
    {
        _handler.RespondWith(HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference: must be unique - that value has been taken."]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            CreateClient().CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.True(exception.IsReferenceConflict);
        Assert.Contains("must be unique", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task DoesNotMistakeOtherValidationFailuresForAReferenceConflict()
    {
        _handler.RespondWith(HttpStatusCode.UnprocessableEntity,
            """{"errors":["No payment method was on file for the $299.00 balance"]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            CreateClient().CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest()));

        Assert.False(exception.IsReferenceConflict);
    }

    [Fact]
    public async Task ReportsAnUnreachableBillingSystemAsATransportFailure()
    {
        _handler.Throw(new HttpRequestException("no route to host"));

        await Assert.ThrowsAsync<MaxioTransportException>(() => CreateClient().ReadSiteAsync());
    }

    [Fact]
    public async Task KeepsReferencesOutOfLoggedPaths()
    {
        _handler.RespondWith(HttpStatusCode.InternalServerError, """{"errors":["boom"]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            CreateClient().ReadCustomerByReferenceAsync("eshop:demouser@microsoft.com"));

        Assert.Equal("customers/lookup.json", exception.RequestPath);
        Assert.DoesNotContain("demouser", exception.Message);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private string _body = "{}";
        private Exception? _exception;

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastRequestBody { get; private set; }

        public void RespondWith(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
            _exception = null;
        }

        public void Throw(Exception exception) => _exception = exception;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_exception is not null)
            {
                throw _exception;
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }
}
