using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// A correction to a draft bill. Only the fields provided are changed. The amount is not correctable here —
/// it comes from the order.
/// </summary>
public class UpdateInvoiceRequest : BaseRequest
{
    /// <summary>The bill to correct. Taken from the route.</summary>
    public int InvoiceId { get; set; }

    /// <summary>A new due date, or null to leave it unchanged.</summary>
    public DateOnly? DueDate { get; set; }

    public string? CustomerName { get; set; }

    public string? CustomerEmail { get; set; }
}
