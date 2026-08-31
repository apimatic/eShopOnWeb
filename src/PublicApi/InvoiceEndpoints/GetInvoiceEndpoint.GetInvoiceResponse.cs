using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

public class GetInvoiceResponse : BaseResponse
{
    public GetInvoiceResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetInvoiceResponse()
    {
    }

    public int InvoiceId { get; set; }
    public int OrderId { get; set; }
    public string ProviderInvoiceId { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>The bill's stage as eShop drives it: Draft, Issued or Withdrawn.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The fine-grained status the provider reports (e.g. DRAFT, SENT, CANCELED).</summary>
    public string ProviderStatus { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? WithdrawnAt { get; set; }

    /// <summary>The provider's own account of how the bill reached its current state.</summary>
    public List<InvoiceEventDto> History { get; set; } = new();

    /// <summary>How the shopper can pay the bill; present only once it has been put to them.</summary>
    public string? PaymentLink { get; set; }
}
