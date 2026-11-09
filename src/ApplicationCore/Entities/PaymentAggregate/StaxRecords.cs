using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>Links a shopper (by e-mail) to the customer record kept at Stax.</summary>
public class StaxCustomerLink
{
    public string BuyerEmail { get; set; } = string.Empty;
    public string StaxCustomerId { get; set; } = string.Empty;
}

/// <summary>Shop-side record of a monthly invoice schedule created at Stax.</summary>
public class InvoiceScheduleRecord
{
    /// <summary>The Stax schedule id.</summary>
    public string Id { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string StaxCustomerId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime FirstInvoiceDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
