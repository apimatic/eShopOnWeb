namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Instruction to create a subscription for an existing Maxio customer.
/// <paramref name="UniquenessToken"/> is Maxio's optional duplicate-prevention token; when supplied,
/// a repeated request carrying the same token within the dedup window is rejected with 409. The
/// service leaves it null and instead relies on a per-user lock plus a pre-flight lookup for
/// idempotency (see <see cref="SubscriptionService"/>), which also allows retries after a failure.
/// </summary>
public record NewSubscription(long CustomerId, string ProductHandle, string? UniquenessToken);
