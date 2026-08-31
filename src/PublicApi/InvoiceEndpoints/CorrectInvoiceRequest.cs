using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Corrects the due date and/or the customer details a bill carries. The amount is not correctable here —
/// what is billed comes from the order. Any field left null is left unchanged.
/// </summary>
public class CorrectInvoiceRequest : BaseRequest
{
    /// <summary>The bill to correct. Bound from the route, not the body.</summary>
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>The corrected calendar due date, or null to leave it unchanged.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>The corrected customer name, or null to leave it unchanged.</summary>
    public string? CustomerName { get; set; }

    /// <summary>The corrected customer email, or null to leave it unchanged.</summary>
    public string? CustomerEmail { get; set; }
}
