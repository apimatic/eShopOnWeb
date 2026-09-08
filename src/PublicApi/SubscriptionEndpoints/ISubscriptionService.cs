using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public enum SubscribeFailureKind
{
    None = 0,
    UserNotFound,
    PlanNotFound,
    AlreadyTerminatedSubscription
}

public sealed class SubscribeResult
{
    public bool Succeeded => Failure == SubscribeFailureKind.None && Subscription is not null;

    public SubscribeFailureKind Failure { get; init; } = SubscribeFailureKind.None;

    public SubscriptionDto? Subscription { get; init; }

    /// <summary>True when a new subscription was created; false on an idempotent replay.</summary>
    public bool Created { get; init; }

    public string? FailureDetail { get; init; }
}

public sealed class MySubscriptionsResult
{
    public bool UserFound { get; init; }

    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } =
        new List<SubscriptionDto>();
}

/// <summary>
/// Application service that owns the "subscribe" business flow against Maxio Advanced Billing:
/// ensure a Maxio customer exists for the eShopOnWeb user (idempotently), enroll them on a plan,
/// and list their subscriptions. Idempotency guarantees a double-click can never create two
/// Maxio customers or two subscriptions for the same user + plan.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the subscribable plans in the configured Maxio product family.</summary>
    Task<IReadOnlyList<SubscriptionPlanDto>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>Ensures a Maxio customer and subscribes the given user to the given plan handle.</summary>
    Task<SubscribeResult> SubscribeAsync(string userName, string productHandle, CancellationToken cancellationToken);

    /// <summary>Returns the current user's subscriptions from Maxio.</summary>
    Task<MySubscriptionsResult> ListMySubscriptionsAsync(string userName, CancellationToken cancellationToken);
}
