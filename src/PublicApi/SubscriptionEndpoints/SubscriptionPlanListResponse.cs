using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListResponse
{
    public Guid CorrelationId { get; set; }
    public List<SubscriptionPlanItem> Plans { get; set; } = new();

    public class SubscriptionPlanItem
    {
        public string Handle { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal PriceInCents { get; set; }
        public string PriceFormatted => $"{PriceInCents / 100:C}";
        public string State { get; set; } = "";
    }
}
