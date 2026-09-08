using System;

namespace Microsoft.eShopWeb.Infrastructure.Data.Billing;

public class BillingSubscription
{
    public int Id { get; set; }

    public int BillingAccountId { get; set; }

    public BillingAccount BillingAccount { get; set; }

    public long MaxioSubscriptionId { get; set; }

    public string ProductHandle { get; set; }

    public DateTime CreatedUtc { get; set; }
}
