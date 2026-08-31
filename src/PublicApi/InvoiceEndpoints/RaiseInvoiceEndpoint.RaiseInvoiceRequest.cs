using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class RaiseInvoiceRequest : BaseRequest
{
    /// <summary>The calendar date the bill falls due.</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>Optional customer name to carry on the bill. Defaults to the shopper's identity.</summary>
    public string? CustomerName { get; set; }

    /// <summary>Optional customer email to carry on the bill. Defaults to the shopper's identity.</summary>
    public string? CustomerEmail { get; set; }

    /// <summary>The order to bill — taken from the route, not the request body.</summary>
    [JsonIgnore]
    public int OrderId { get; set; }
}
