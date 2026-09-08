using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Data.Billing;

public class BillingAccount
{
    public int Id { get; set; }

    public string AppUserId { get; set; }

    public string AppUserName { get; set; }

    public string Email { get; set; }

    public string MaxioReference { get; set; }

    public long? MaxioCustomerId { get; set; }

    public string FirstName { get; set; }

    public string LastName { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public List<BillingSubscription> Subscriptions { get; set; } = new List<BillingSubscription>();
}
