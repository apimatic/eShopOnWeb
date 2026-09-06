using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The eShopOnWeb identity being billed, as resolved from the caller's token.
/// </summary>
/// <param name="UserId">The eShopOnWeb Identity user id.</param>
/// <param name="Email">The user's email address; also the natural key used to build the billing customer reference.</param>
public sealed record Subscriber(string UserId, string Email)
{
    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? Organization { get; init; }

    /// <summary>
    /// Stable, provider-agnostic reference for this shopper. Deterministic on purpose: it is what makes
    /// "ensure the billing customer exists" idempotent across requests, processes and app restarts
    /// (eShopOnWeb user ids are regenerated when running on the in-memory provider, email addresses are not).
    /// </summary>
    public string BillingReference
    {
        get
        {
            Guard.Against.NullOrWhiteSpace(Email, nameof(Email));
            return $"eshoponweb-{Email.Trim().ToLowerInvariant()}";
        }
    }
}
