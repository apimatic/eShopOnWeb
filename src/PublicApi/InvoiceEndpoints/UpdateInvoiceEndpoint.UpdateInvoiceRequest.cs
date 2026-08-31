using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Corrects the due date and/or customer details a draft bill carries. The amount is not
/// correctable here — it always comes from the order. Any field left null is left unchanged.
/// </summary>
public class UpdateInvoiceRequest : BaseRequest
{
    public DateOnly? DueDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }

    /// <summary>Set server-side from the route.</summary>
    [JsonIgnore]
    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>Set server-side from the authenticated caller.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}
