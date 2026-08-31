using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class UpdateInvoiceRequest : BaseRequest
{
    /// <summary>New calendar due date. Omit to leave it unchanged.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>New customer name to carry on the bill. Omit to leave it unchanged.</summary>
    public string? CustomerName { get; set; }

    /// <summary>New customer email to carry on the bill. Omit to leave it unchanged.</summary>
    public string? CustomerEmail { get; set; }

    /// <summary>The bill to correct — taken from the route, not the request body.</summary>
    [JsonIgnore]
    public string InvoiceId { get; set; } = string.Empty;
}
