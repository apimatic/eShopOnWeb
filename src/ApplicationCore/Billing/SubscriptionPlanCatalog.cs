using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The plans offered for subscription.
/// </summary>
/// <param name="Truncated">True when the billing system held more plans than were read; the list is then partial.</param>
public record SubscriptionPlanCatalog(string ProductFamilyHandle, IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);
