using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioApiClientTests
{
    private static MaxioApiClient CreateClient(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://acme.chargify.com/") },
            NullLogger<MaxioApiClient>.Instance);

    [Fact]
    public async Task ReadCustomerByReference_ReturnsNullWhenTheCustomerDoesNotExist()
    {
        var handler = StubHttpMessageHandler.Returning(HttpStatusCode.NotFound, "");
        var client = CreateClient(handler);

        var customer = await client.ReadCustomerByReferenceAsync("eshoponweb:demouser@microsoft.com");

        Assert.Null(customer);
        Assert.Equal(
            "/customers/lookup.json?reference=eshoponweb%3Ademouser%40microsoft.com",
            handler.Requests.Single().RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task ReadCustomerByReference_UnwrapsTheResponseEnvelope()
    {
        var handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.OK,
            """{"customer":{"id":42,"first_name":"Ada","last_name":"Lovelace","email":"ada@example.com","reference":"eshoponweb:ada@example.com"}}""");

        var customer = await CreateClient(handler).ReadCustomerByReferenceAsync("eshoponweb:ada@example.com");

        Assert.NotNull(customer);
        Assert.Equal(42, customer!.Id);
        Assert.Equal("Ada", customer.FirstName);
        Assert.Equal("eshoponweb:ada@example.com", customer.Reference);
    }

    [Fact]
    public async Task CreateCustomer_SendsTheSnakeCasedRequestEnvelopeAndOmitsUnsetMembers()
    {
        var handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.Created,
            """{"customer":{"id":7,"reference":"eshoponweb:ada@example.com"}}""");

        var customer = await CreateClient(handler).CreateCustomerAsync(new CreateCustomer
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@example.com",
            Reference = "eshoponweb:ada@example.com"
        });

        Assert.Equal(7, customer.Id);

        var body = handler.RequestBodies.Single();
        Assert.Contains("\"first_name\":\"Ada\"", body);
        Assert.Contains("\"last_name\":\"Lovelace\"", body);
        Assert.Contains("\"reference\":\"eshoponweb:ada@example.com\"", body);
        Assert.DoesNotContain("organization", body);
        Assert.StartsWith("{\"customer\":", body);
    }

    [Fact]
    public async Task CreateSubscription_SendsTheSubscriptionEnvelope()
    {
        var handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.Created,
            """{"subscription":{"id":99,"state":"active","product":{"handle":"eshop-pro","name":"Pro Plan"}}}""");

        var subscription = await CreateClient(handler).CreateSubscriptionAsync(new CreateSubscription
        {
            ProductHandle = "eshop-pro",
            CustomerId = 7,
            Reference = "eshoponweb:ada@example.com|eshop-pro|1",
            PaymentCollectionMethod = "remittance"
        });

        Assert.Equal(99, subscription.Id);
        Assert.Equal("eshop-pro", subscription.Product?.Handle);

        var body = handler.RequestBodies.Single();
        Assert.StartsWith("{\"subscription\":", body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":7", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
    }

    [Fact]
    public async Task ListProductsForProductFamily_FollowsPaginationUntilAShortPage()
    {
        var fullPage = "[" + string.Join(",", Enumerable.Range(1, 200)
            .Select(id => "{\"product\":{\"id\":" + id + ",\"handle\":\"plan-" + id + "\"}}")) + "]";

        var handler = new StubHttpMessageHandler((_, callNumber) => StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            callNumber == 1 ? fullPage : """[{"product":{"id":201,"handle":"plan-201"}}]"""));

        var products = await CreateClient(handler).ListProductsForProductFamilyAsync(3026729);

        Assert.Equal(201, products.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("page=1&per_page=200&include_archived=false", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("page=2", handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task NonSuccessResponses_BecomeAMaxioApiExceptionCarryingTheParsedErrors()
    {
        var handler = StubHttpMessageHandler.Returning(
            HttpStatusCode.UnprocessableEntity,
            """{"errors":["No payment method was on file for the $299.00 balance"]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient(handler).CreateSubscriptionAsync(new CreateSubscription { ProductHandle = "eshop-pro" }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.True(exception.IsClientError);
        Assert.Equal("No payment method was on file for the $299.00 balance", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task AnUnconfiguredClientFailsWithAConfigurationError()
    {
        var client = new MaxioApiClient(
            new HttpClient(StubHttpMessageHandler.Returning(HttpStatusCode.OK, "[]")),
            NullLogger<MaxioApiClient>.Instance);

        await Assert.ThrowsAsync<BillingNotConfiguredException>(() => client.ListProductFamiliesAsync());
    }

    [Fact]
    public void TheTypedClientIsConfiguredWithTheSpecServerTemplateAndBasicAuth()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Maxio:ApiKey"] = "test-key",
                ["Maxio:Subdomain"] = "acme",
                ["Maxio:ProductFamilyHandle"] = "eshop-subscribe"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaxioSubscriptionBilling(configuration);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(MaxioServiceCollectionExtensions.HttpClientName);

        Assert.Equal(new Uri("https://acme.chargify.com/"), client.BaseAddress);
        Assert.Equal("Basic", client.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal(
            "test-key:x",
            Encoding.UTF8.GetString(Convert.FromBase64String(client.DefaultRequestHeaders.Authorization!.Parameter!)));
    }

    [Fact]
    public void TheTypedClientIsLeftUnconfiguredWhenCredentialsAreMissing()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMaxioSubscriptionBilling(configuration);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(MaxioServiceCollectionExtensions.HttpClientName);

        Assert.Null(client.BaseAddress);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }
}
