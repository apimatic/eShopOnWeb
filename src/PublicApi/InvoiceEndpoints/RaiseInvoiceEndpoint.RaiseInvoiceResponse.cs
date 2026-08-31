using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class RaiseInvoiceResponse : BaseResponse
{
    public RaiseInvoiceResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RaiseInvoiceResponse()
    {
    }

    /// <summary>The provider's identifier for the raised bill (top-level, drives the later actions).</summary>
    public string InvoiceId { get; set; } = string.Empty;

    public int OrderId { get; set; }

    /// <summary>eShop's lifecycle state; a freshly raised bill is not yet put to the shopper (Draft).</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>The status the provider reports for the bill.</summary>
    public string ProviderStatus { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
}
