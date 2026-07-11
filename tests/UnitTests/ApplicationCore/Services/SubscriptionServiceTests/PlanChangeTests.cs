using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.UnitTests.Builders;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SubscriptionServiceTests;

public class PlanChangeTests
{
    private readonly IBillingClient _mockBillingClient = Substitute.For<IBillingClient>();
    private readonly IPublisher _mockPublisher = Substitute.For<IPublisher>();
    private readonly IAppLogger<SubscriptionService> _mockLogger = Substitute.For<IAppLogger<SubscriptionService>>();
    private readonly SubscriptionBuilder _builder = new();

    private SubscriptionService CreateService() => new(_mockBillingClient, _mockPublisher, _mockLogger);

    [Fact]
    public async Task PreviewRejectsWhenTargetPlanIsTheCurrentPlan()
    {
        var active = _builder.Active();
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>())
            .Returns(active);

        var service = CreateService();

        await Assert.ThrowsAsync<PlanChangeException>(() => service.PreviewPlanChangeAsync(
            SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId, SubscriptionBuilder.TestProductHandle, true));

        await _mockBillingClient.DidNotReceive().PreviewPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewRejectsChangingACanceledSubscription()
    {
        var canceled = _builder.WithState("canceled");
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>())
            .Returns(canceled);

        var service = CreateService();

        await Assert.ThrowsAsync<InvalidSubscriptionTransitionException>(() => service.PreviewPlanChangeAsync(
            SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId, SubscriptionBuilder.TestOtherProductHandle, true));
    }

    [Fact]
    public async Task PreviewReturnsANonEmptyCommitToken()
    {
        var active = _builder.Active();
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>())
            .Returns(active);
        var rawPreview = new PlanChangePreview(SubscriptionBuilder.TestSubscriptionId, active.ProductHandle, SubscriptionBuilder.TestOtherProductHandle, true, 100, 200, 300, 0, commitToken: "");
        _mockBillingClient.PreviewPlanChangeAsync(SubscriptionBuilder.TestSubscriptionId, SubscriptionBuilder.TestOtherProductHandle, true, Arg.Any<CancellationToken>())
            .Returns(rawPreview);

        var service = CreateService();
        var preview = await service.PreviewPlanChangeAsync(
            SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId, SubscriptionBuilder.TestOtherProductHandle, true);

        Assert.False(string.IsNullOrEmpty(preview.CommitToken));
    }

    [Fact]
    public async Task CommitRejectsAStaleCommitToken()
    {
        var active = _builder.Active();
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>())
            .Returns(active);
        var freshPreview = new PlanChangePreview(SubscriptionBuilder.TestSubscriptionId, active.ProductHandle, SubscriptionBuilder.TestOtherProductHandle, true, 100, 200, 300, 0, commitToken: "");
        _mockBillingClient.PreviewPlanChangeAsync(SubscriptionBuilder.TestSubscriptionId, SubscriptionBuilder.TestOtherProductHandle, true, Arg.Any<CancellationToken>())
            .Returns(freshPreview);

        var service = CreateService();

        await Assert.ThrowsAsync<PlanChangeException>(() => service.CommitPlanChangeAsync(
            SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId, SubscriptionBuilder.TestOtherProductHandle, true, "a-stale-token-from-a-different-preview"));

        await _mockBillingClient.DidNotReceive().CommitPlanChangeAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitAppliesTheChangeWhenTheTokenMatchesAFreshPreview()
    {
        var active = _builder.Active();
        _mockBillingClient.GetSubscriptionAsync(SubscriptionBuilder.TestSubscriptionId, Arg.Any<CancellationToken>())
            .Returns(active);
        var freshPreview = new PlanChangePreview(SubscriptionBuilder.TestSubscriptionId, active.ProductHandle, SubscriptionBuilder.TestOtherProductHandle, true, 100, 200, 300, 0, commitToken: "");
        _mockBillingClient.PreviewPlanChangeAsync(SubscriptionBuilder.TestSubscriptionId, SubscriptionBuilder.TestOtherProductHandle, true, Arg.Any<CancellationToken>())
            .Returns(freshPreview);
        var updated = _builder.WithState("active");
        _mockBillingClient.CommitPlanChangeAsync(SubscriptionBuilder.TestSubscriptionId, SubscriptionBuilder.TestOtherProductHandle, true, Arg.Any<CancellationToken>())
            .Returns(updated);

        // Replicates SubscriptionService's private commit-token format for a preview matching 'freshPreview' above.
        var matchingToken = string.Join(
            ':',
            SubscriptionBuilder.TestSubscriptionId.ToString(CultureInfo.InvariantCulture),
            SubscriptionBuilder.TestOtherProductHandle,
            true,
            (long?)100,
            (long?)200,
            (long?)300,
            (long?)0);

        var service = CreateService();
        var result = await service.CommitPlanChangeAsync(
            SubscriptionBuilder.TestBuyerId, false, SubscriptionBuilder.TestSubscriptionId, SubscriptionBuilder.TestOtherProductHandle, true, matchingToken);

        Assert.Equal(updated.Id, result.Id);
        await _mockPublisher.Received(1).Publish(Arg.Any<INotification>(), Arg.Any<CancellationToken>());
    }
}
