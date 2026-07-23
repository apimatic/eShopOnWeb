using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// When the provider cannot be reached at all, the seam must still surface its own typed failure.
/// A raw transport exception escaping here is what turns a friendly "plans unavailable" message
/// into a 500.
/// </summary>
public class MaxioBillingClientTransportTests
{
    /// <summary>A transport that fails the way an unreachable host does.</summary>
    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("Connection refused (localhost:6299)");
    }

    /// <summary>A transport that never answers, the way a hung provider does.</summary>
    private sealed class TimingOutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
    }

    private static MaxioBillingClient Create(HttpMessageHandler handler)
    {
        var settings = BillingClientFixture.DefaultSettings();
        settings.BaseUrl = "http://localhost:6299";

        return new MaxioBillingClient(
            new HttpClient(handler) { BaseAddress = new Uri(settings.ResolveBaseUrl()) },
            Options.Create(settings),
            Substitute.For<IAppLogger<MaxioBillingClient>>());
    }

    [Fact]
    public async Task ListPlansAsync_SurfacesAnUnreachableProvider_AsATypedProviderError()
    {
        var ex = await Assert.ThrowsAsync<BillingProviderException>(
            () => Create(new UnreachableHandler()).ListPlansAsync());

        Assert.Contains("list plans", ex.Message);
    }

    [Fact]
    public async Task ListPlansAsync_SurfacesATimeout_AsATypedProviderError()
    {
        await Assert.ThrowsAsync<BillingProviderException>(
            () => Create(new TimingOutHandler()).ListPlansAsync());
    }

    [Fact]
    public async Task FindPlanByHandleAsync_SurfacesAnUnreachableProvider()
    {
        await Assert.ThrowsAsync<BillingProviderException>(
            () => Create(new UnreachableHandler()).FindPlanByHandleAsync("eshop-pro"));
    }

    [Fact]
    public async Task FindCustomerAsync_SurfacesAnUnreachableProvider()
    {
        await Assert.ThrowsAsync<BillingProviderException>(
            () => Create(new UnreachableHandler()).FindCustomerAsync("demouser@microsoft.com"));
    }

    [Fact]
    public async Task ListSubscriptionsAsync_SurfacesAnUnreachableProvider()
    {
        await Assert.ThrowsAsync<BillingProviderException>(
            () => Create(new UnreachableHandler()).ListSubscriptionsAsync("demouser@microsoft.com"));
    }

    [Fact]
    public async Task GetSubscriptionAsync_SurfacesAnUnreachableProvider()
    {
        await Assert.ThrowsAsync<BillingProviderException>(
            () => Create(new UnreachableHandler()).GetSubscriptionAsync(900001));
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SurfacesAnUnreachableProvider()
    {
        await Assert.ThrowsAsync<BillingProviderException>(
            () => Create(new UnreachableHandler()).CreateSubscriptionAsync(51234, "eshop-pro"));
    }

    [Fact]
    public async Task EnsureCustomerAsync_SurfacesAnUnreachableProvider()
    {
        await Assert.ThrowsAsync<BillingProviderException>(
            () => Create(new UnreachableHandler()).EnsureCustomerAsync(new SubscriberIdentity("demouser@microsoft.com")));
    }

    [Fact]
    public async Task GetMeteredComponentAsync_SurfacesAnUnreachableProvider()
    {
        await Assert.ThrowsAsync<BillingProviderException>(
            () => Create(new UnreachableHandler()).GetMeteredComponentAsync());
    }

    [Fact]
    public async Task RecordUsageAsync_SurfacesAnUnreachableProvider()
    {
        await Assert.ThrowsAsync<BillingProviderException>(
            () => Create(new UnreachableHandler()).RecordUsageAsync(900001, 1, "memo"));
    }

    [Fact]
    public async Task PauseSubscriptionAsync_SurfacesAnUnreachableProvider()
    {
        await Assert.ThrowsAsync<BillingProviderException>(
            () => Create(new UnreachableHandler()).PauseSubscriptionAsync(900001));
    }

    [Fact]
    public async Task CancelSubscriptionAsync_SurfacesAnUnreachableProvider()
    {
        await Assert.ThrowsAsync<BillingProviderException>(
            () => Create(new UnreachableHandler())
                .CancelSubscriptionAsync(900001, CancellationTiming.Immediate, reason: null));
    }

    [Fact]
    public async Task ChangePlanAsync_SurfacesAnUnreachableProvider()
    {
        await Assert.ThrowsAsync<BillingProviderException>(
            () => Create(new UnreachableHandler())
                .ChangePlanAsync(900001, "basic-plan", PlanChangeTiming.Immediate));
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAsCancellation_NotAsABillingFailure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // A cancellation the caller asked for must not be reported as a provider outage.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(new TimingOutHandler()).ListPlansAsync(cts.Token));
    }
}
