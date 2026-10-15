using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <param name="Plans">The plans offered.</param>
/// <param name="IsTruncated">True when the listing stopped at its page cap, so <paramref name="Plans"/> may be incomplete.</param>
public sealed record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);
