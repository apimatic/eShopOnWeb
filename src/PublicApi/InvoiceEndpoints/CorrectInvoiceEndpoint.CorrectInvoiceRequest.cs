using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Corrects the due date and/or customer details a bill carries. The amount is not
/// correctable here — it always comes from the order. Any omitted field is left unchanged.
/// </summary>
public class CorrectInvoiceRequest : BaseRequest
{
    /// <summary>The provider invoice id (bound from the route).</summary>
    public string InvoiceId { get; set; } = string.Empty;

    public DateOnly? DueDate { get; set; }

    public string? CustomerName { get; set; }

    public string? CustomerEmail { get; set; }
}
