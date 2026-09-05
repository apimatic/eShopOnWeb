namespace Microsoft.eShopWeb.ApplicationCore.Models.Billing;

/// <summary>
/// Everything needed to enroll an eShopOnWeb buyer into a Maxio subscription.
/// </summary>
/// <param name="BuyerReference">
/// Stable per-buyer identifier used as the Maxio customer's <c>reference</c>. In this app the
/// Identity username (== email) already plays this role for baskets/orders (see
/// <c>Buyer.IdentityGuid</c>), so it is reused here.
/// </param>
public record SubscriptionEnrollmentRequest(string BuyerReference, string Email, string PlanHandle);
