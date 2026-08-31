using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Corrects the due date and/or the customer details a bill carries, while it has not yet been put
/// to the shopper. Omitted fields are left unchanged. The amount is not correctable here.
/// </summary>
public class CorrectInvoiceRequest : BaseRequest
{
    public DateOnly? DueDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
}
