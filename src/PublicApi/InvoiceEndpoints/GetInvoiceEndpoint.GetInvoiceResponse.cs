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

    public string InvoiceId { get; set; } = string.Empty;

    /// <summary>The eShop order this bill was raised against; null for a bill eShop did not raise.</summary>
    public int? OrderId { get; set; }

    /// <summary>The current state the provider reports for the bill.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>eShop's lifecycle state; null for a bill eShop did not raise.</summary>
    public string? State { get; set; }

    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }

    /// <summary>How the bill can be paid; present only once it has been put to the shopper.</summary>
    public string? PaymentLink { get; set; }

    /// <summary>Whatever the provider reports about how the bill reached its current state.</summary>
    public List<InvoiceHistoryEntryDto> History { get; set; } = new();
}
