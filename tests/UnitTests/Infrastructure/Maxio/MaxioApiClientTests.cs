using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioApiClientTests
{
    [Fact]
    public async Task LookingUpAnUnknownCustomerReturnsNullRatherThanThrowing()
    {
        // Maxio answers 404 for a reference it has never seen; that is an expected outcome of the
        // "does this shopper already exist?" question, not a failure.
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.NotFound, "{}");

        var customer = await CreateClient(handler).FindCustomerByReferenceAsync("eshoponweb-nobody");

        Assert.Null(customer);
    }

    [Fact]
    public async Task LookingUpACustomerUnwrapsTheEnvelopeAndMapsSnakeCaseFields()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK,
            """{"customer":{"id":42,"reference":"eshoponweb-demo","first_name":"Demo","last_name":"User","email":"demo@example.com"}}""");

        var customer = await CreateClient(handler).FindCustomerByReferenceAsync("eshoponweb-demo");

        Assert.NotNull(customer);
        Assert.Equal(42, customer!.Id);
        Assert.Equal("Demo", customer.FirstName);
        Assert.Equal("demo@example.com", customer.Email);
        Assert.Contains("reference=eshoponweb-demo", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task ProductFamiliesAreAddressedByHandleNotByNumericId()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.OK,
            """[{"product":{"id":1,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}}]""");

        var products = await CreateClient(handler).ListProductsForFamilyAsync("eshop-subscribe");

        Assert.Single(products);
        Assert.Equal("eshop-pro", products[0].Handle);
        Assert.Equal(29900, products[0].PriceInCents);
        Assert.Contains("product_families/handle:eshop-subscribe/products.json", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task CreateSubscriptionSendsTheDocumentedSnakeCasePayloadWithAUniquenessToken()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.Created,
            """{"subscription":{"id":7,"state":"active","reference":"eshoponweb-demo--eshop-pro"}}""");

        await CreateClient(handler).CreateSubscriptionAsync(
            new MaxioSubscriptionAttributes
            {
                ProductHandle = "eshop-pro",
                CustomerId = 42,
                Reference = "eshoponweb-demo--eshop-pro",
                PaymentCollectionMethod = "remittance"
            },
            "11111111-2222-3333-4444-555555555555");

        using var document = JsonDocument.Parse(handler.RequestBodies[0]);
        var root = document.RootElement;
        var subscription = root.GetProperty("subscription");

        Assert.Equal("eshop-pro", subscription.GetProperty("product_handle").GetString());
        Assert.Equal(42, subscription.GetProperty("customer_id").GetInt64());
        Assert.Equal("remittance", subscription.GetProperty("payment_collection_method").GetString());
        Assert.Equal("11111111-2222-3333-4444-555555555555", root.GetProperty("uniqueness_token").GetString());
    }

    [Fact]
    public async Task ErrorArraysAreSurfacedOnTheException()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference: must be unique - that value has been taken."]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient(handler).CreateCustomerAsync(new MaxioCustomerAttributes { Reference = "taken" }));

        Assert.True(exception.IsReferenceTaken);
        Assert.False(exception.IsDuplicateSubmission);
    }

    [Fact]
    public async Task DuplicateSubmissionsAreRecognisedFromTheConflictPayload()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.Conflict,
            """{"errors":["DuplicatePrevention::DuplicateSubmissionError"]}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient(handler).CreateSubscriptionAsync(new MaxioSubscriptionAttributes(), "token"));

        Assert.True(exception.IsDuplicateSubmission);
    }

    [Fact]
    public async Task FieldKeyedErrorObjectsAreFlattenedIntoReadableMessages()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.UnprocessableEntity,
            """{"errors":{"customer":"is invalid"}}""");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient(handler).CreateCustomerAsync(new MaxioCustomerAttributes()));

        Assert.Contains("customer: is invalid", exception.Message);
    }

    [Fact]
    public async Task ANonJsonErrorBodyStillProducesAUsefulMessage()
    {
        var handler = new StubHttpMessageHandler().Respond(HttpStatusCode.BadGateway, "<html>gateway down</html>");

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient(handler).CreateCustomerAsync(new MaxioCustomerAttributes()));

        Assert.Contains("gateway down", exception.Message);
    }

    [Fact]
    public async Task AnUnreachableBillingSystemIsReportedAsAnUpstreamOutage()
    {
        var handler = new StubHttpMessageHandler().Throw(new HttpRequestException("connection refused"));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient(handler).GetSiteAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public async Task ATimedOutCallIsReportedAsAnUpstreamOutageRatherThanACancellation()
    {
        // HttpClient reports its own timeout as a cancellation; that must not escape as an
        // unhandled error when the caller never asked to cancel.
        var handler = new StubHttpMessageHandler().Throw(new TaskCanceledException("timed out"));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => CreateClient(handler).GetSiteAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public async Task CallerCancellationIsNotDisguisedAsAnUpstreamFailure()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var handler = new StubHttpMessageHandler().Throw(new TaskCanceledException("cancelled"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient(handler).GetSiteAsync(cancellation.Token));
    }

    private static MaxioApiClient CreateClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://test-site.chargify.com/") },
            NullLogger<MaxioApiClient>.Instance);
}
