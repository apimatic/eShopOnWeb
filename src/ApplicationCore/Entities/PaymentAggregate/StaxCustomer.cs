using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>Links a shop buyer (their user name) to the customer record kept at Stax.</summary>
public class StaxCustomer
{
    public string BuyerId { get; set; } = string.Empty;
    public string StaxCustomerId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
