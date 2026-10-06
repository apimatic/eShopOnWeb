using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public string ProductFamilyHandle { get; set; } = string.Empty;

    public List<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();

    /// <summary>
    /// True when the billing system offers more plans than were read; <see cref="Plans"/> is then partial.
    /// </summary>
    public bool Truncated { get; set; }
}
