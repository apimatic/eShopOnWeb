using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.BillingClient;

/// <summary>
/// Every provider failure has to reach callers as a typed exception, so nothing upstream needs to
/// reason about HTTP or Maxio's several error shapes.
/// </summary>
public class ErrorTranslationTests
{
    [Fact]
    public async Task RejectedCredentialsSurfaceAsAnAuthenticationFailure()
    {
        // Maxio answers a bad API key with plain text, not JSON.
        var handler = new StubHttpMessageHandler()
            .RespondWithText(HttpStatusCode.Unauthorized, MaxioResponses.UnauthorizedText);

        var client = BillingClientBuilder.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderAuthenticationException>(
            () => client.ListPlansAsync());

        Assert.Equal(401, exception.StatusCode);
    }

    [Fact]
    public async Task ForbiddenAlsoSurfacesAsAnAuthenticationFailure()
    {
        var handler = new StubHttpMessageHandler().RespondWith(HttpStatusCode.Forbidden, string.Empty);
        var client = BillingClientBuilder.Build(handler);

        await Assert.ThrowsAsync<BillingProviderAuthenticationException>(() => client.ListPlansAsync());
    }

    [Fact]
    public async Task AnArrayOfProviderErrorsIsSurfacedAsAValidationFailure()
    {
        var handler = new StubHttpMessageHandler().RespondWith((HttpStatusCode)422, MaxioResponses.ErrorArray);
        var client = BillingClientBuilder.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderValidationException>(
            () => client.PreviewPlanChangeAsync(93491347, "basic-plan"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("Subscription must be active", Assert.Single(exception.ProviderErrors));
        Assert.Contains("Subscription must be active", exception.Message);
    }

    [Fact]
    public async Task EveryProviderErrorInAnArrayIsPreserved()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith((HttpStatusCode)422, MaxioResponses.ErrorArrayMultiple);

        var client = BillingClientBuilder.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderValidationException>(
            () => client.CreateSubscriptionAsync("shopper@example.com", "eshop-pro"));

        Assert.Equal(2, exception.ProviderErrors.Count);
        Assert.Contains("Bank routing number: cannot be blank.", exception.ProviderErrors);
        Assert.Contains("Bank account number: cannot be blank.", exception.ProviderErrors);
    }

    [Fact]
    public async Task TheSingularErrorShapeIsUnderstoodToo()
    {
        // Cancelling an already-cancelled subscription answers {"error": "..."}, not {"errors": [...]}.
        var handler = new StubHttpMessageHandler()
            .RespondWith((HttpStatusCode)422, MaxioResponses.ErrorSingular);

        var client = BillingClientBuilder.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderValidationException>(
            () => client.CancelAsync(93491347, CancellationTiming.Immediate, null));

        Assert.Equal("The subscription is already canceled", Assert.Single(exception.ProviderErrors));
    }

    [Fact]
    public async Task TheFieldMapErrorShapeIsUnderstoodToo()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.NotFound, string.Empty)
            .RespondWith((HttpStatusCode)422, MaxioResponses.ErrorFieldMap);

        var client = BillingClientBuilder.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderValidationException>(
            () => client.EnsureCustomerAsync("shopper@example.com", "shopper@example.com", null, null));

        Assert.Equal("can't be blank", Assert.Single(exception.ProviderErrors));
    }

    [Fact]
    public async Task ANotFoundOnAnOperationThatRequiresTheEntitySurfacesAsNotFound()
    {
        var handler = new StubHttpMessageHandler().RespondWith(HttpStatusCode.NotFound, string.Empty);
        var client = BillingClientBuilder.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderNotFoundException>(
            () => client.ResumeAsync(999999999));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task AServerFailureSurfacesAsAGeneralProviderException()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.InternalServerError, string.Empty);

        var client = BillingClientBuilder.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Equal(500, exception.StatusCode);

        // A server failure is not a validation or authentication problem.
        Assert.IsType<BillingProviderException>(exception);
    }

    [Fact]
    public async Task AnUnreachableProviderSurfacesAsAProviderExceptionRatherThanAnHttpError()
    {
        var handler = new StubHttpMessageHandler().RespondWithTransportFailure();
        var client = BillingClientBuilder.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Null(exception.StatusCode);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task AnUnreadableResponseSurfacesAsAProviderException()
    {
        var handler = new StubHttpMessageHandler().RespondWithJson("this is not json");
        var client = BillingClientBuilder.Build(handler);

        await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());
    }

    [Fact]
    public async Task RateLimitingSurfacesAsAProviderException()
    {
        var handler = new StubHttpMessageHandler().RespondWith((HttpStatusCode)429, string.Empty);
        var client = BillingClientBuilder.Build(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Equal(429, exception.StatusCode);
    }

    [Fact]
    public async Task ABadRequestSurfacesAsAValidationFailure()
    {
        var handler = new StubHttpMessageHandler()
            .RespondWith(HttpStatusCode.BadRequest, MaxioResponses.ErrorArray);

        var client = BillingClientBuilder.Build(handler);

        await Assert.ThrowsAsync<BillingProviderValidationException>(
            () => client.ChangePlanAsync(93491347, "basic-plan", PlanChangeTiming.Immediate));
    }

    [Fact]
    public void EveryTypedBillingFailureIsABillingProviderException()
    {
        // Callers that only want "something went wrong at the provider" can catch the base type.
        Assert.IsAssignableFrom<BillingProviderException>(
            new BillingProviderAuthenticationException("x"));
        Assert.IsAssignableFrom<BillingProviderException>(new BillingProviderNotFoundException("x"));
        Assert.IsAssignableFrom<BillingProviderException>(new BillingProviderValidationException("x"));
    }

    [Fact]
    public void AProviderExceptionWithNoReportedErrorsExposesAnEmptyCollection()
    {
        var exception = new BillingProviderException("boom");

        Assert.Empty(exception.ProviderErrors);
        Assert.Null(exception.StatusCode);
    }
}
