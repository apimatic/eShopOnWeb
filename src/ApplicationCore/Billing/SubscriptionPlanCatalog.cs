using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// The plans of the configured product family. <see cref="Truncated"/> is true when the page cap was hit
/// with a full last page, i.e. the provider may hold more plans than <see cref="Plans"/> lists.
/// </summary>
public sealed record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);
