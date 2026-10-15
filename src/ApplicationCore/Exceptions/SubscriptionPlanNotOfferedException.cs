using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotOfferedException : Exception
{
    public SubscriptionPlanNotOfferedException(string? planHandle)
        : base(string.IsNullOrWhiteSpace(planHandle)
            ? "A plan handle is required."
            : $"Plan '{planHandle}' is not offered. See GET /api/subscription-plans for the plans on offer.")
    {
    }
}
