using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Infrastructure.MaxioBillingClientTests;

public class ErrorHandling
{
    private const string LookupPath = "customers/lookup.json?reference=demouser@microsoft.com";
    private const string Reference = "demouser@microsoft.com";

    private readonly MaxioClientBuilder _builder = new();

    [Fact]
    public async Task RejectedCredentialsProduceAnActionableUnauthorizedError()
    {
        _builder.Handler.RespondWith(HttpMethod.Get, "product_families.json", HttpStatusCode.Unauthorized,
            "HTTP Basic: Access denied.");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().ListPlansAsync());

        Assert.Equal(401, exception.StatusCode);
        Assert.Contains("Maxio:ApiKey", exception.Message);
    }

    [Fact]
    public async Task ObjectShapedErrorsAreFlattenedIntoMessages()
    {
        _builder.Handler
            .RespondWith(HttpMethod.Get, LookupPath, HttpStatusCode.NotFound, string.Empty)
            .RespondWith(HttpMethod.Post, "customers.json", HttpStatusCode.UnprocessableEntity,
                MaxioPayloads.ErrorMap);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => _builder.Build()
            .EnsureCustomerAsync(Reference, Reference, "demouser", "microsoft.com"));

        // Customer validation returns {"errors":{"customer":"can't be blank"}} rather than an array.
        Assert.Contains("customer: can't be blank", exception.Errors);
    }

    [Fact]
    public async Task ANonJsonErrorBodyIsStillReported()
    {
        _builder.Handler.RespondWith(HttpMethod.Get, "product_families.json", HttpStatusCode.BadRequest,
            "A valid product_family_id is required");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().ListPlansAsync());

        Assert.Equal(400, exception.StatusCode);
        Assert.Contains("A valid product_family_id is required", exception.Errors);
    }

    [Fact]
    public async Task AnUnreachableProviderBecomesATypedException()
    {
        _builder.Handler.FailWith(HttpMethod.Get, "product_families.json",
            new HttpRequestException("No such host is known."));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().ListPlansAsync());

        Assert.Equal(0, exception.StatusCode);
        Assert.Contains("could not be reached", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task ATimeoutBecomesATypedException()
    {
        _builder.Handler.FailWith(HttpMethod.Get, "product_families.json",
            new TaskCanceledException("The request timed out."));

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().ListPlansAsync());

        Assert.Contains("did not respond in time", exception.Message);
    }

    [Fact]
    public async Task AnUnreadableResponseBodyBecomesATypedException()
    {
        _builder.Handler.RespondWith(HttpMethod.Get, "product_families.json", HttpStatusCode.OK,
            "this is not json");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => _builder.Build().ListPlansAsync());

        Assert.Contains("could not be read", exception.Message);
    }

    [Fact]
    public async Task A404IsOnlyToleratedWhereAbsenceIsAValidAnswer()
    {
        // Lookups tolerate 404 (an unknown reference simply has no subscriptions)...
        _builder.Handler.RespondWith(HttpMethod.Get, LookupPath, HttpStatusCode.NotFound, string.Empty);
        Assert.Empty(await _builder.Build().ListSubscriptionsAsync(Reference));

        // ...but a 404 on an action is a real failure and must not be swallowed.
        var writeBuilder = new MaxioClientBuilder();
        writeBuilder.Handler.RespondWith(HttpMethod.Put, "subscriptions/15236915/reactivate.json",
            HttpStatusCode.NotFound, string.Empty);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => writeBuilder.Build().ReactivateAsync(15236915));

        Assert.Equal(404, exception.StatusCode);
    }
}
