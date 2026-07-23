using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests;

/// <summary>
/// Lifecycle transitions (UC4). Each action must take its own provider route — an immediate cancel and an
/// end-of-period cancel are different operations with very different consequences for the customer.
/// </summary>
public class MaxioBillingClientLifecycleTests
{
    [Fact]
    public async Task Pause_HoldsTheSubscription()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription("on_hold"));

        var updated = await context.Client.ApplyLifecycleActionAsync(
            MaxioPayloads.SubscriptionId, SubscriptionLifecycleAction.Pause, null);

        var request = Assert.Single(context.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("hold", request.Path);

        Assert.Equal(SubscriptionState.Paused, updated.State);
        Assert.Contains(SubscriptionLifecycleAction.Resume, updated.AllowedActions);
    }

    [Fact]
    public async Task Resume_TakesTheSubscriptionBackToActive()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription("active"));

        var updated = await context.Client.ApplyLifecycleActionAsync(
            MaxioPayloads.SubscriptionId, SubscriptionLifecycleAction.Resume, null);

        var request = Assert.Single(context.Handler.Requests);
        Assert.Contains("resume", request.Path);
        Assert.Equal(SubscriptionState.Active, updated.State);
    }

    [Fact]
    public async Task CancelImmediately_DeletesTheSubscription()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription("canceled"));

        var updated = await context.Client.ApplyLifecycleActionAsync(
            MaxioPayloads.SubscriptionId, SubscriptionLifecycleAction.CancelImmediately, null);

        var request = Assert.Single(context.Handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.DoesNotContain("delayed", request.Path);

        Assert.Equal(SubscriptionState.Cancelled, updated.State);
        Assert.Equal(new[] { SubscriptionLifecycleAction.Reactivate }, updated.AllowedActions.ToArray());
    }

    [Fact]
    public async Task CancelImmediately_SendsTheReasonWhenOneIsGiven()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription("canceled"));

        await context.Client.ApplyLifecycleActionAsync(
            MaxioPayloads.SubscriptionId, SubscriptionLifecycleAction.CancelImmediately, "too expensive");

        var request = Assert.Single(context.Handler.Requests);
        Assert.NotNull(request.Body);
        Assert.Contains("too expensive", request.Body!);
    }

    [Fact]
    public async Task CancelImmediately_SendsNoCancellationMessageWhenNoReasonIsGiven()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription("canceled"));

        await context.Client.ApplyLifecycleActionAsync(
            MaxioPayloads.SubscriptionId, SubscriptionLifecycleAction.CancelImmediately, "   ");

        var request = Assert.Single(context.Handler.Requests);

        // A whitespace-only reason must not become an empty cancellation message on the customer's record.
        Assert.DoesNotContain("cancellation_message", request.Body ?? string.Empty);
    }

    [Fact]
    public async Task CancelAtEndOfPeriod_SchedulesTheCancellationAndRefreshesTheSubscription()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.DelayedCancellationAccepted);
        context.Handler.Enqueue(MaxioPayloads.SubscriptionPendingCancel);

        var updated = await context.Client.ApplyLifecycleActionAsync(
            MaxioPayloads.SubscriptionId, SubscriptionLifecycleAction.CancelAtEndOfPeriod, null);

        // The delayed-cancel endpoint returns only a message, so the subscription is re-read afterwards.
        Assert.Equal(2, context.Handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, context.Handler.Requests[0].Method);
        Assert.NotEqual(HttpMethod.Delete, context.Handler.Requests[0].Method);

        // Still active, but with a cancellation pending at the period boundary.
        Assert.Equal(SubscriptionState.Active, updated.State);
        Assert.True(updated.CancellationPending);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.FromHours(-4)),
            updated.CancellationScheduledAt);
    }

    [Fact]
    public async Task Reactivate_BringsACancelledSubscriptionBack()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.Subscription("active"));

        var updated = await context.Client.ApplyLifecycleActionAsync(
            MaxioPayloads.SubscriptionId, SubscriptionLifecycleAction.Reactivate, null);

        var request = Assert.Single(context.Handler.Requests);
        Assert.Contains("reactivate", request.Path);
        Assert.Equal(SubscriptionState.Active, updated.State);
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_SurfacesAProviderRejection()
    {
        using var context = new BillingTestContext();
        context.Handler.Enqueue(MaxioPayloads.ErrorList, HttpStatusCode.UnprocessableEntity);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.ApplyLifecycleActionAsync(
                MaxioPayloads.SubscriptionId, SubscriptionLifecycleAction.Resume, null));

        Assert.NotNull(exception);
    }

    [Fact]
    public async Task ApplyLifecycleActionAsync_RejectsAnUndefinedAction()
    {
        using var context = new BillingTestContext();

        await Assert.ThrowsAsync<BillingProviderException>(
            () => context.Client.ApplyLifecycleActionAsync(
                MaxioPayloads.SubscriptionId, (SubscriptionLifecycleAction)99, null));

        Assert.Empty(context.Handler.Requests);
    }
}
