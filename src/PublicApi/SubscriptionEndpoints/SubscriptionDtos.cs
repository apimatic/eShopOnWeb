using System.Collections.Generic;
namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints
{
    public class PlanDto
    {
        public string Handle { get; set; } = "";
        public string Name { get; set; } = "";
        public int PriceInCents { get; set; }
        public int Interval { get; set; }
        public string IntervalUnit { get; set; } = "";
    }
    public class SubscriptionDto
    {
        public int Id { get; set; }
        public string State { get; set; } = "";
        public string ProductHandle { get; set; } = "";
        public string ProductName { get; set; } = "";
        public int PriceInCents { get; set; }
        public string CurrentPeriodEndsAt { get; set; } = "";
    }
    public class CreateSubscriptionRequest
    {
        public string ProductHandle { get; set; } = "";
    }
}
