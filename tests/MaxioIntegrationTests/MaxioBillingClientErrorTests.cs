using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Failure handling at the seam: every provider, transport and configuration failure must reach callers
/// as one of the integration's own typed exceptions, never as a raw SDK or HTTP exception.
/// </summary>
public class MaxioBillingClientErrorTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 401)]
    [InlineData(HttpStatusCode.Forbidden, 403)]
    [InlineData(HttpStatusCode.TooManyRequests, 429)]
    [InlineData(HttpStatusCode.InternalServerError, 500)]
    [InlineData(HttpStatusCode.BadGateway, 502)]
    public async Task ProviderFailures_SurfaceAsBillingProviderException_CarryingTheStatus(
        HttpStatusCode status,
        int expected)
    {
        var handler = new FakeMaxioHandler().Enqueue(status, """{"error":"nope"}""");
        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.ListSubscriptionsAsync(500123));

        Assert.Equal(expected, exception.StatusCode);
        Assert.Equal("list customer subscriptions", exception.Operation);
    }

    [Fact]
    public async Task ProviderFailures_IncludeTheProvidersOwnMessage_ForDiagnosis()
    {
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.Unauthorized, """{"error":"API key is invalid"}""");

        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.GetSubscriptionAsync(60001));

        Assert.Contains("API key is invalid", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderFailures_NeverEchoTheApiKeyBackToTheCaller()
    {
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.Unauthorized, """{"error":"unauthorized"}""");

        var (client, logger) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.GetSubscriptionAsync(60001));

        Assert.DoesNotContain(TestClientFactory.ApiKey, exception.Message, StringComparison.Ordinal);
        Assert.All(logger.Warnings,
            warning => Assert.DoesNotContain(TestClientFactory.ApiKey, warning, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProviderFailures_AreTruncated_SoAHugeErrorBodyCannotFloodTheCaller()
    {
        var huge = new string('x', 5_000);
        var handler = new FakeMaxioHandler().Enqueue(HttpStatusCode.InternalServerError, $"\"{huge}\"");

        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.GetSubscriptionAsync(60001));

        Assert.True(exception.Message.Length < 700, $"Message was {exception.Message.Length} characters long.");
        Assert.EndsWith("...", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectionFailures_SurfaceAsBillingProviderException_WithNoStatus()
    {
        var handler = new FakeMaxioHandler().AlwaysFailToConnect();
        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.GetSubscriptionAsync(60001));

        Assert.Null(exception.StatusCode);
        Assert.Contains("could not be reached", exception.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task ProviderFailures_AreLogged_SoAnOutageIsVisibleInTheHostsLogs()
    {
        var handler = new FakeMaxioHandler().Enqueue(HttpStatusCode.BadGateway, """{"error":"down"}""");
        var (client, logger) = TestClientFactory.Create(handler);

        await Assert.ThrowsAsync<BillingProviderException>(() => client.GetSubscriptionAsync(60001));

        Assert.Contains(logger.Warnings, warning => warning.Contains("502", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ACancelRejection_ReadsTheProvidersSingleMessageErrorShape()
    {
        // Cancellation is the one operation whose 422 body is a union of two different error shapes.
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.UnprocessableEntity,
                MaxioPayloads.SingleError("Subscription is already canceled."));

        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CancelSubscriptionAsync(60001, "done"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("already canceled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACancelRejection_ReadsTheProvidersErrorListShape()
    {
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.UnprocessableEntity,
                MaxioPayloads.ValidationErrors("Reason code: is not valid.", "Subscription: cannot be cancelled."));

        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CancelSubscriptionAsync(60001, "done"));

        Assert.Contains("Reason code: is not valid.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot be cancelled.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownSubscriptionOnDelayedCancellation_SurfacesAsANotFoundFailure()
    {
        var handler = new FakeMaxioHandler().Enqueue(HttpStatusCode.NotFound, "");

        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.CancelSubscriptionAtPeriodEndAsync(60001, null));

        Assert.Equal(404, exception.StatusCode);
        Assert.Equal("schedule end-of-period cancellation", exception.Operation);
    }

    [Fact]
    public async Task AnUnknownProductFamilyWhenListingPlans_SurfacesAsANotFoundFailure()
    {
        var handler = new FakeMaxioHandler()
            .EnqueueOk(MaxioPayloads.ProductFamilies((3026728, "eshop-subscribe")))
            .Enqueue(HttpStatusCode.NotFound, "\"Product family not found\"");

        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(() => client.ListPlansAsync());

        Assert.Equal(404, exception.StatusCode);
        Assert.Equal("list plans", exception.Operation);
    }

    [Fact]
    public async Task APreviewRejection_SurfacesTheProvidersValidationMessage()
    {
        var handler = new FakeMaxioHandler()
            .Enqueue(HttpStatusCode.UnprocessableEntity,
                MaxioPayloads.ValidationErrors("Product: cannot be migrated to."));

        var (client, _) = TestClientFactory.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.PreviewPlanChangeAsync(60001, "eshop-pro"));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("cannot be migrated to.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructing_TheClient_FailsFast_WhenTheIntegrationIsNotConfigured()
    {
        var unconfigured = new Infrastructure.Configuration.MaxioSettings();

        var exception = Assert.Throws<BillingConfigurationException>(() =>
            new Infrastructure.Services.MaxioBillingClient(
                new HttpClient(new FakeMaxioHandler()),
                Options.Create(unconfigured),
                new RecordingLogger<Infrastructure.Services.MaxioBillingClient>()));

        Assert.Contains("Maxio:ApiKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACancellationTheCallerRequested_PropagatesUnchanged_AndIsNotMaskedAsAProviderOutage()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var handler = new FakeMaxioHandler();
        var (client, _) = TestClientFactory.Create(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetSubscriptionAsync(60001, cancellation.Token));
    }
}
