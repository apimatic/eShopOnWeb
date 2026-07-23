using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

/// <summary>
/// Provider failures must reach callers as one typed exception carrying the provider's own
/// message, never as a raw transport or parsing error.
/// </summary>
public class ErrorHandling
{
    private readonly RecordingHttpMessageHandler _handler = new();

    private static string ResumePath => $"/subscriptions/{MaxioResponses.SubscriptionId}/resume.json";

    private static string SubscriptionPath => $"/subscriptions/{MaxioResponses.SubscriptionId}.json";

    [Fact]
    public async Task SurfacesAProviderRejectionWithItsStatusCodeAndMessages()
    {
        _handler.RespondJson(HttpMethod.Post, ResumePath, MaxioResponses.ErrorsArray, (HttpStatusCode)422);

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            client.ResumeAsync(MaxioResponses.SubscriptionId));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("Only subscriptions that are on hold can be resumed.",
            Assert.Single(exception.ProviderErrors));
        Assert.Contains("Only subscriptions that are on hold can be resumed.", exception.Message);
    }

    /// <summary>Validation failures arrive keyed by field rather than as a flat list.</summary>
    [Fact]
    public async Task SurfacesFieldKeyedValidationErrors()
    {
        _handler.RespondJson(HttpMethod.Post, ResumePath, MaxioResponses.ErrorsObject, (HttpStatusCode)422);

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            client.ResumeAsync(MaxioResponses.SubscriptionId));

        Assert.Equal("product_handle: must be specified", Assert.Single(exception.ProviderErrors));
    }

    [Fact]
    public async Task SurfacesAServerErrorEvenWithoutAStructuredBody()
    {
        _handler.RespondJson(HttpMethod.Post, ResumePath, "<html>gateway error</html>", HttpStatusCode.InternalServerError);

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            client.ResumeAsync(MaxioResponses.SubscriptionId));

        Assert.Equal(500, exception.StatusCode);
        Assert.Empty(exception.ProviderErrors);
    }

    [Fact]
    public async Task SurfacesUnauthorizedCredentialsAsAProviderFailure()
    {
        _handler.RespondStatus(HttpMethod.Get, MaxioResponses.FamilyPath, HttpStatusCode.Unauthorized);

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Equal(401, exception.StatusCode);
    }

    /// <summary>
    /// A 404 on a mutation is a real failure, unlike a 404 on a lookup which simply means "absent".
    /// </summary>
    [Fact]
    public async Task TreatsANotFoundOnAMutationAsAFailureRatherThanAnAbsentRecord()
    {
        _handler.RespondStatus(HttpMethod.Post, ResumePath, HttpStatusCode.NotFound);

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            client.ResumeAsync(MaxioResponses.SubscriptionId));

        Assert.Equal(404, exception.StatusCode);
    }

    [Fact]
    public async Task SurfacesAnUnreachableProviderAsAProviderFailure()
    {
        _handler.RespondThrows(HttpMethod.Get, MaxioResponses.FamilyPath, new HttpRequestException("no such host"));

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Contains("could not be reached", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Null(exception.StatusCode);
    }

    [Fact]
    public async Task SurfacesAProviderTimeoutAsAProviderFailure()
    {
        _handler.RespondThrows(HttpMethod.Get, MaxioResponses.FamilyPath, new TaskCanceledException("timed out"));

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Contains("did not respond in time", exception.Message);
    }

    /// <summary>A caller's own cancellation is not a provider fault and must propagate unchanged.</summary>
    [Fact]
    public async Task LetsCallerCancellationPropagateInsteadOfMaskingItAsAProviderFailure()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        _handler.RespondThrows(HttpMethod.Get, MaxioResponses.FamilyPath, new OperationCanceledException());

        var client = TestBillingClientFactory.Create(_handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ListPlansAsync(cancellation.Token));
    }

    [Fact]
    public async Task SurfacesAnUnparseableResponseAsAProviderFailure()
    {
        _handler.RespondJson(HttpMethod.Get, SubscriptionPath, "{ this is not json ]");

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            client.GetSubscriptionAsync(MaxioResponses.SubscriptionId));

        Assert.Contains("could not be understood", exception.Message);
    }

    /// <summary>
    /// A 200 with an empty envelope would otherwise dereference to null and surface as a
    /// NullReferenceException far from the cause.
    /// </summary>
    [Fact]
    public async Task SurfacesAnEmptyEnvelopeOnAMutationAsAProviderFailure()
    {
        _handler.RespondJson(HttpMethod.Post, ResumePath, "{}");

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() =>
            client.ResumeAsync(MaxioResponses.SubscriptionId));

        Assert.Contains("no subscription record", exception.Message);
    }

    [Fact]
    public async Task SurfacesAnEmptyEnvelopeAfterRecordingUsageAsAProviderFailure()
    {
        var usagesPath = $"/subscriptions/{MaxioResponses.SubscriptionId}/components/{MaxioResponses.ComponentId}/usages.json";
        _handler.RespondJson(HttpMethod.Post, usagesPath, "{}");

        var client = TestBillingClientFactory.Create(_handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.RecordUsageAsync(
            new RecordUsageRequest(MaxioResponses.SubscriptionId, MaxioResponses.ComponentId, 1)));

        Assert.Contains("no usage receipt", exception.Message);
    }

    [Fact]
    public void CarriesNoProviderErrorsWhenConstructedWithoutAny()
    {
        var exception = new BillingProviderException("something went wrong");

        Assert.Empty(exception.ProviderErrors);
        Assert.Null(exception.StatusCode);
    }
}
