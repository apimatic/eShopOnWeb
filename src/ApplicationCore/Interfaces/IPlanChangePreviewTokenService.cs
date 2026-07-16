using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Seals/unseals the opaque <see cref="PlanChangePreview.PreviewToken"/> that round-trips a plan-change
/// preview to its commit, so <c>SubscriptionService</c> can detect tampering or staleness before ever
/// calling the billing provider to commit.
/// </summary>
public interface IPlanChangePreviewTokenService
{
    string Protect(PlanChangePreviewPayload payload);

    /// <summary>Returns false if the token is malformed, was tampered with, or has expired.</summary>
    bool TryUnprotect(string token, out PlanChangePreviewPayload? payload);
}
