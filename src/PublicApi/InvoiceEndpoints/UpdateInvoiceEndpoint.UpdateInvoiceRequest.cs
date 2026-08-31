using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// A correction to a draft bill. Any field left null is left unchanged. The amount is deliberately
/// absent — what is billed comes from the order and is not correctable here.
/// </summary>
public class UpdateInvoiceRequest : BaseRequest
{
    public DateOnly? DueDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }

    [JsonIgnore]
    public int InvoiceId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}
