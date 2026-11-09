using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>Links a shopper (by email) to the customer record created for them at Stax.</summary>
public class StaxCustomerLink : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string StaxCustomerId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
