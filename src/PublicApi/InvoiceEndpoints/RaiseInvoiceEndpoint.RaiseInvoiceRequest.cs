using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Raises a bill with the provider for an order. What is billed comes from the order itself; the
/// only thing the caller supplies is the calendar date the bill falls due.
/// </summary>
public class RaiseInvoiceRequest : BaseRequest
{
    /// <summary>The calendar date the bill falls due (ISO <c>yyyy-MM-dd</c>).</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>Set server-side from the route.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>Set server-side from the authenticated caller.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}
