using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// A correction to a draft bill. Any field left null is unchanged. The billed amount is not here:
/// what is billed always comes from the order.
/// </summary>
public class UpdateInvoiceRequest
{
    public DateOnly? DueDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
}
