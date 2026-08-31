using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class RaiseInvoiceRequest : BaseRequest
{
    /// <summary>The calendar date the bill falls due (yyyy-MM-dd).</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>The order to bill, taken from the route.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }

    /// <summary>The caller, taken from the token — never bound from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}
