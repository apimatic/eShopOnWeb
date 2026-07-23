using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// Customer lookup and creation — the idempotency seam for UC1. A missing reference must read as "absent",
/// not as a failure, or every repeat subscribe would explode instead of reusing the customer.
/// </summary>
public class MaxioBillingClientCustomerTests
{
    [Fact]
    public async Task FindCustomerByReferenceAsync_MapsTheCustomer()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Customer);

        var customer = await context.Client.FindCustomerByReferenceAsync(MaxioPayloads.CustomerReference);

        Assert.NotNull(customer);
        Assert.Equal(MaxioPayloads.CustomerId, customer!.Id);
        Assert.Equal(MaxioPayloads.CustomerReference, customer.Reference);
        Assert.Equal(MaxioPayloads.CustomerReference, customer.Email);
        Assert.Equal("Demo", customer.FirstName);
        Assert.Equal("User", customer.LastName);
    }

    [Fact]
    public async Task FindCustomerByReferenceAsync_ReturnsNull_WhenTheReferenceIsUnknown()
    {
        using var context = new BillingTestContext();
        context.Handler.EnqueueStatus(HttpStatusCode.NotFound);

        Assert.Null(await context.Client.FindCustomerByReferenceAsync("nobody@example.test"));
    }

    [Fact]
    public async Task FindCustomerByReferenceAsync_ReturnsNull_ForABlankReference_WithoutCallingMaxio()
    {
        using var context = new BillingTestContext();

        Assert.Null(await context.Client.FindCustomerByReferenceAsync("  "));
        Assert.Empty(context.Handler.Requests);
    }

    [Fact]
    public async Task FindCustomerByReferenceAsync_SurfacesOtherFailuresAsBillingProviderException()
    {
        using var context = new BillingTestContext();
        context.Handler.EnqueueStatus(HttpStatusCode.Unauthorized);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.FindCustomerByReferenceAsync(MaxioPayloads.CustomerReference));

        // A bad API key must never be mistaken for "this customer does not exist".
        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task CreateCustomerAsync_SendsTheStableReference()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Customer);

        var created = await context.Client.CreateCustomerAsync(new BillingCustomerRegistration(
            MaxioPayloads.CustomerReference, MaxioPayloads.CustomerReference, "Demo", "User"));

        var request = Assert.Single(context.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.NotNull(request.Body);

        // The reference is what makes repeat subscribes idempotent — it must reach Maxio.
        Assert.Contains("\"reference\"", request.Body!);
        Assert.Contains(MaxioPayloads.CustomerReference, request.Body!);
        Assert.Contains("\"first_name\"", request.Body!);
        Assert.Contains("\"last_name\"", request.Body!);

        Assert.Equal(MaxioPayloads.CustomerId, created.Id);
    }

    [Fact]
    public async Task CreateCustomerAsync_SurfacesRejectionAsBillingProviderException()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.ErrorList, HttpStatusCode.UnprocessableEntity);

        await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.CreateCustomerAsync(new BillingCustomerRegistration(
                MaxioPayloads.CustomerReference, MaxioPayloads.CustomerReference, "Demo", "User")));
    }

    [Fact]
    public async Task CreateCustomerAsync_RejectsANullRegistration()
    {
        using var context = new BillingTestContext();

        await Assert.ThrowsAsync<ArgumentNullException>(() => context.Client.CreateCustomerAsync(null!));
        Assert.Empty(context.Handler.Requests);
    }
}
