using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests.Client;

public class MaxioApiClientTests
{
    private const string ApiKey = "test-api-key";

    [Fact]
    public async Task Sends_the_api_key_as_basic_auth_with_the_literal_password_x()
    {
        var stub = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, """{"site":{"currency":"USD"}}""");
        var client = BuildClient(stub, withAuthentication: true);

        await client.ReadSiteAsync();

        var authorization = stub.Requests.Single().Headers.Authorization;
        Assert.NotNull(authorization);
        Assert.Equal("Basic", authorization!.Scheme);
        Assert.Equal($"{ApiKey}:x", Encoding.UTF8.GetString(Convert.FromBase64String(authorization.Parameter!)));
    }

    [Fact]
    public async Task Addresses_the_product_family_by_handle_and_pages_through_results()
    {
        var stub = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.OK, """[{"product":{"id":1,"handle":"pro","price_in_cents":100}}]""");

        var client = BuildClient(stub);

        var products = await client.ListProductsForProductFamilyAsync("handle:eshop-subscribe");

        var uri = stub.Requests.Single().RequestUri!;
        Assert.Equal("/product_families/handle%3Aeshop-subscribe/products.json", uri.AbsolutePath);
        Assert.Contains("page=1", uri.Query);
        Assert.Contains("per_page=200", uri.Query);
        Assert.Equal("pro", Assert.Single(products).Handle);
    }

    [Fact]
    public async Task Follows_pagination_until_a_short_page_arrives()
    {
        var fullPage = "[" + string.Join(",",
            Enumerable.Range(1, 200).Select(i => "{\"product\":{\"id\":" + i + ",\"handle\":\"p" + i + "\"}}")) + "]";

        var stub = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.OK, fullPage)
            .Respond(HttpStatusCode.OK, """[{"product":{"id":201,"handle":"p201"}}]""");

        var client = BuildClient(stub);

        var products = await client.ListProductsForProductFamilyAsync("handle:family");

        Assert.Equal(201, products.Count);
        Assert.Equal(2, stub.Requests.Count);
        Assert.Contains("page=2", stub.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task Treats_a_missing_customer_as_null_rather_than_a_failure()
    {
        var stub = new StubHttpMessageHandler().Respond(HttpStatusCode.NotFound);
        var client = BuildClient(stub);

        Assert.Null(await client.ReadCustomerByReferenceAsync("nobody"));
    }

    [Fact]
    public async Task Escapes_the_reference_it_looks_customers_up_by()
    {
        var stub = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, """{"customer":{"id":7}}""");
        var client = BuildClient(stub);

        await client.ReadCustomerByReferenceAsync("a b&c");

        Assert.Equal("?reference=a%20b%26c", stub.Requests.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task Surfaces_provider_validation_messages()
    {
        var stub = new StubHttpMessageHandler().Respond(
            HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference: must be unique - that value has been taken."]}""");

        var client = BuildClient(stub);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CreateCustomerAsync(new MaxioCreateCustomer { Email = "a@b.com" }));

        Assert.Equal(422, exception.ProviderStatusCode);
        Assert.True(exception.IsCallerFault);
        Assert.Contains("must be unique", Assert.Single(exception.ProviderErrors));
    }

    [Fact]
    public async Task Reports_a_refused_credential_without_leaking_it()
    {
        var stub = new StubHttpMessageHandler().Respond(HttpStatusCode.Unauthorized, "HTTP Basic: Access denied.");
        var client = BuildClient(stub, withAuthentication: true);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ReadSiteAsync());

        Assert.Equal(401, exception.ProviderStatusCode);
        Assert.False(exception.IsCallerFault);
        Assert.DoesNotContain(ApiKey, exception.ToString());
    }

    [Fact]
    public async Task Sends_the_create_subscription_payload_the_specification_describes()
    {
        var stub = new StubHttpMessageHandler().Respond(
            HttpStatusCode.Created,
            """{"subscription":{"id":42,"state":"active"}}""");

        var client = BuildClient(stub);

        await client.CreateSubscriptionAsync(new MaxioCreateSubscription
        {
            CustomerId = 7,
            ProductHandle = "eshop-pro",
            Reference = "ref-1",
            PaymentCollectionMethod = "remittance"
        });

        var body = stub.RequestBodies.Single();
        Assert.Equal("/subscriptions.json", stub.Requests.Single().RequestUri!.AbsolutePath);
        Assert.Contains("\"subscription\":", body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":7", body);
        Assert.Contains("\"reference\":\"ref-1\"", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
    }

    [Fact]
    public async Task Omits_unset_optional_attributes_instead_of_sending_null()
    {
        var stub = new StubHttpMessageHandler().Respond(HttpStatusCode.OK, """{"customer":{"id":1}}""");
        var client = BuildClient(stub);

        await client.CreateCustomerAsync(new MaxioCreateCustomer
        {
            FirstName = "Demo",
            LastName = "Shopper",
            Email = "demo@example.com",
            Reference = "ref"
        });

        Assert.DoesNotContain("organization", stub.RequestBodies.Single());
    }

    [Fact]
    public async Task Ignores_response_properties_it_does_not_know()
    {
        var stub = new StubHttpMessageHandler().Respond(
            HttpStatusCode.OK,
            """{"customer":{"id":9,"reference":"r","a_brand_new_field":{"nested":true}}}""");

        var client = BuildClient(stub);

        var customer = await client.ReadCustomerByReferenceAsync("r");

        Assert.Equal(9, customer!.Id);
    }

    private static MaxioApiClient BuildClient(StubHttpMessageHandler stub, bool withAuthentication = false)
    {
        var options = new MaxioOptions
        {
            ApiKey = ApiKey,
            Subdomain = "test-site",
            ProductFamilyHandle = "family",
            MaxRetryAttempts = 0
        };

        HttpMessageHandler pipeline = stub;
        if (withAuthentication)
        {
            pipeline = new MaxioAuthenticationHandler(new StaticOptionsMonitor<MaxioOptions>(options))
            {
                InnerHandler = stub
            };
        }

        var httpClient = new HttpClient(pipeline)
        {
            BaseAddress = options.ResolveBaseAddress()
        };

        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);
    }
}

/// <summary>An <see cref="IOptionsMonitor{TOptions}"/> over a fixed value.</summary>
internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    public StaticOptionsMonitor(T value) => CurrentValue = value;

    public T CurrentValue { get; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
