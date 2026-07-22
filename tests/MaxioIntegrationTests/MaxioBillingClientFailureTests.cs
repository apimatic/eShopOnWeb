using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

public class MaxioBillingClientFailureTests
{
    [Fact]
    public async Task RejectedCredentialsSurfaceAsAnAuthenticationFailure()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.Unauthorized, """{"error":"Unauthorized"}""");

        var exception = await Assert.ThrowsAsync<BillingAuthenticationException>(
            () => builder.Build().CreateSubscriptionAsync(88833369, "eshop-pro"));

        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task AFailureMessageNeverCarriesTheApiKey()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.Unauthorized, """{"errors":["Bad credentials"]}""");

        var exception = await Assert.ThrowsAsync<BillingAuthenticationException>(
            () => builder.Build().CreateSubscriptionAsync(88833369, "eshop-pro"));

        Assert.DoesNotContain(MaxioClientBuilder.ApiKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownSubscriptionOnAWriteSurfacesAsNotFound()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.NotFound, """{"errors":["Subscription not found"]}""");

        var exception = await Assert.ThrowsAsync<BillingEntityNotFoundException>(
            () => builder.Build().PauseSubscriptionAsync(404404));

        Assert.Contains("Subscription not found", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProviderValidationErrorKeepsTheProvidersOwnMessages()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.UnprocessableEntity, MaxioJson.ErrorArray);

        var exception = await Assert.ThrowsAsync<BillingValidationException>(
            () => builder.Build().RecordUsageAsync(15236915, "api-call", 5, null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("Quantity: must be greater than 0.", Assert.Single(exception.ProviderErrors));
    }

    [Fact]
    public async Task AnObjectShapedErrorEnvelopeIsAlsoUnderstood()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.UnprocessableEntity, MaxioJson.ErrorObject);

        var exception = await Assert.ThrowsAsync<BillingValidationException>(
            () => builder.Build().CreateCustomerAsync("demouser@microsoft.com", "demouser@microsoft.com"));

        Assert.Equal("customer: can't be blank", Assert.Single(exception.ProviderErrors));
    }

    [Fact]
    public async Task APersistentServerFailureSurfacesAsProviderUnavailable()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.AlwaysRespondWith(HttpStatusCode.InternalServerError, "boom");

        var exception = await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => builder.Build().ListPlansAsync());

        Assert.Equal(500, exception.StatusCode);
    }

    [Fact]
    public async Task AnUnreachableProviderSurfacesAsProviderUnavailable()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => builder.Build().CreateSubscriptionAsync(88833369, "eshop-pro"));
    }

    [Fact]
    public async Task AnUnreadableResponseIsReportedRatherThanMisparsed()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.RespondWith(HttpStatusCode.OK, "<html>maintenance</html>");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().GetSubscriptionAsync(15236915));

        Assert.Contains("could not be understood", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATransientReadIsRetriedUntilItSucceeds()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport
            .RespondWith(HttpStatusCode.ServiceUnavailable)
            .RespondWith(HttpStatusCode.OK, MaxioJson.ProductList(MaxioJson.ProPlanProduct));

        var plans = await builder.Build().ListPlansAsync();

        Assert.Single(plans);
        Assert.Equal(2, builder.Transport.CountOf(HttpMethod.Get, "/products.json"));
    }

    [Fact]
    public async Task ReadRetriesAreBounded()
    {
        var builder = new MaxioClientBuilder().WithMaxRetryAttempts(3);
        builder.Transport.AlwaysRespondWith(HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<BillingProviderUnavailableException>(() => builder.Build().ListPlansAsync());

        Assert.Equal(3, builder.Transport.Requests.Count);
    }

    [Fact]
    public async Task AFailedUsageReportIsNeverResent()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.AlwaysRespondWith(HttpStatusCode.ServiceUnavailable);

        await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => builder.Build().RecordUsageAsync(15236915, "api-call", 250, null));

        // Resending would risk billing the same units twice.
        Assert.Equal(1, builder.Transport.CountOf(HttpMethod.Post, "/usages.json"));
    }

    [Fact]
    public async Task AFailedPlanChangeIsNeverResent()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport.AlwaysRespondWith(HttpStatusCode.BadGateway);

        await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => builder.Build().ChangePlanAsync(15236915, "basic-plan", PlanChangeTiming.ImmediateWithProration));

        Assert.Equal(1, builder.Transport.CountOf(HttpMethod.Post, "/migrations.json"));
    }

    [Fact]
    public async Task AnUnreachableProviderDoesNotResendAWrite()
    {
        var builder = new MaxioClientBuilder();
        builder.Transport
            .RespondWith(new HttpRequestException("connection reset"))
            .AlwaysRespondWith(HttpStatusCode.OK, MaxioJson.ActiveSubscription);

        await Assert.ThrowsAsync<BillingProviderUnavailableException>(
            () => builder.Build().PauseSubscriptionAsync(15236915));

        Assert.Single(builder.Transport.Requests);
    }
}
