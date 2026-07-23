using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Client;

/// <summary>Customer lookup and idempotent creation on the eShopOnWeb user reference (UC1 step 3).</summary>
public class MaxioBillingClientCustomerTests
{
    private static readonly CustomerRegistration Registration =
        CustomerRegistration.FromUserReference(MaxioPayloads.CustomerReference);

    [Fact]
    public async Task FindsAnExistingCustomerByReference()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Get, "/customers/lookup.json", MaxioPayloads.Customer));

        var customer = await harness.Client.FindCustomerAsync(MaxioPayloads.CustomerReference);

        Assert.NotNull(customer);
        Assert.Equal(MaxioPayloads.CustomerId, customer.Id);
        Assert.Equal(MaxioPayloads.CustomerReference, customer.Reference);
        Assert.Contains("reference=", harness.Handler.Requests[0].Query);
    }

    [Fact]
    public async Task ReturnsNoCustomerForAnUnknownReference()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Get, "/customers/lookup.json", MaxioPayloads.NotFound, HttpStatusCode.NotFound));

        Assert.Null(await harness.Client.FindCustomerAsync("nobody@microsoft.com"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReturnsNoCustomerForABlankReferenceWithoutCallingTheProvider(string reference)
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler());

        Assert.Null(await harness.Client.FindCustomerAsync(reference));
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task ReusesTheExistingCustomerInsteadOfCreatingASecondOne()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Get, "/customers/lookup.json", MaxioPayloads.Customer));

        var customer = await harness.Client.EnsureCustomerAsync(Registration);

        Assert.Equal(MaxioPayloads.CustomerId, customer.Id);
        Assert.Empty(harness.Handler.RequestsFor(HttpMethod.Post, "/customers.json"));
    }

    [Fact]
    public async Task CreatesTheCustomerFromTheUserReferenceWhenNoneExists()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Get, "/customers/lookup.json", MaxioPayloads.NotFound, HttpStatusCode.NotFound)
            .Map(HttpMethod.Post, "/customers.json", MaxioPayloads.Customer, HttpStatusCode.Created));

        var customer = await harness.Client.EnsureCustomerAsync(Registration);

        Assert.Equal(MaxioPayloads.CustomerId, customer.Id);

        var body = Assert.Single(harness.Handler.RequestsFor(HttpMethod.Post, "/customers.json")).Body;
        Assert.NotNull(body);
        Assert.Contains($"\"reference\":\"{MaxioPayloads.CustomerReference}\"", body);
        Assert.Contains($"\"email\":\"{MaxioPayloads.CustomerReference}\"", body);
        Assert.Contains("\"first_name\":\"customer\"", body);
    }

    [Fact]
    public async Task TreatsALostCreationRaceAsSuccessByReReadingTheReference()
    {
        var lookupCalls = 0;
        var handler = new StubMaxioHandler()
            .Map(HttpMethod.Get, "/customers/lookup.json", _ =>
                // First lookup misses; after the create is rejected, the second lookup finds the
                // customer another request created concurrently.
                ++lookupCalls == 1
                    ? (HttpStatusCode.NotFound, MaxioPayloads.NotFound)
                    : (HttpStatusCode.OK, MaxioPayloads.Customer))
            .Map(HttpMethod.Post, "/customers.json", """{"errors":{}}""", HttpStatusCode.UnprocessableEntity);

        using var harness = MaxioBillingClientHarness.With(handler);

        var customer = await harness.Client.EnsureCustomerAsync(Registration);

        Assert.Equal(MaxioPayloads.CustomerId, customer.Id);
        Assert.Equal(2, lookupCalls);
    }

    [Fact]
    public async Task SurfacesARejectedCustomerCreationAsATypedException()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Get, "/customers/lookup.json", MaxioPayloads.NotFound, HttpStatusCode.NotFound)
            .Map(HttpMethod.Post, "/customers.json", """{"error":"Email is invalid"}""", HttpStatusCode.BadRequest));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => harness.Client.EnsureCustomerAsync(Registration));

        Assert.Equal(400, exception.StatusCode);
        Assert.DoesNotContain("Email is invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SurfacesALookupFailureRatherThanPretendingTheCustomerIsAbsent()
    {
        using var harness = MaxioBillingClientHarness.With(new StubMaxioHandler()
            .Map(HttpMethod.Get, "/customers/lookup.json", """{"error":"Unauthorized"}""", HttpStatusCode.Unauthorized));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => harness.Client.FindCustomerAsync(MaxioPayloads.CustomerReference));

        Assert.Equal(401, exception.StatusCode);
    }
}
