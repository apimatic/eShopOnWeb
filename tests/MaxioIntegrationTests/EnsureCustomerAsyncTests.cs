using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.TestSupport;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class EnsureCustomerAsyncTests
{
    [Fact]
    public async Task DoesNotCreateWhenCustomerAlreadyResolvesByReference()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "customer": { "id": 555, "email": "shopper@example.com", "reference": "shopper@example.com" } }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        await client.EnsureCustomerAsync("shopper@example.com", "shopper@example.com", "Ada", "Lovelace");

        // Only the reference lookup ran — a queue-exhaustion failure here would mean a create was
        // unexpectedly attempted for a customer that already exists.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DoesNotCreateWhenAnExactEmailMatchIsFoundViaFuzzySearch()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Empty(HttpStatusCode.NotFound),
            SequentialStubHandler.Json(HttpStatusCode.OK, """[{ "customer": { "id": 556, "email": "shopper@example.com" } }]"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        await client.EnsureCustomerAsync("shopper@example.com", "shopper@example.com", "Ada", "Lovelace");

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task CreatesCustomerWhenNoneIsFoundByReferenceOrEmail()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Empty(HttpStatusCode.NotFound),
            SequentialStubHandler.Json(HttpStatusCode.OK, "[]"),
            SequentialStubHandler.Json(HttpStatusCode.OK, """{ "customer": { "id": 557, "email": "new@example.com", "reference": "new@example.com" } }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        await client.EnsureCustomerAsync("new@example.com", "new@example.com", "Ada", "Lovelace");

        Assert.Equal(3, handler.Requests.Count);
        var createBody = handler.RequestBodies[2];
        Assert.Contains("\"reference\":\"new@example.com\"", createBody);
        Assert.Contains("\"first_name\":\"Ada\"", createBody);
    }

    [Fact]
    public async Task ThrowsBillingProviderExceptionWhenCreateIsRejected()
    {
        var handler = new SequentialStubHandler(
            SequentialStubHandler.Empty(HttpStatusCode.NotFound),
            SequentialStubHandler.Json(HttpStatusCode.OK, "[]"),
            SequentialStubHandler.Json(HttpStatusCode.UnprocessableEntity, """{ "errors": { "per_page": null, "price_point": null } }"""));
        var client = TestMaxioBillingClientFactory.Create(handler);

        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.EnsureCustomerAsync("new@example.com", "new@example.com", "Ada", "Lovelace"));

        Assert.Equal(422, ex.StatusCode);
    }
}
