using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>One entry of the provider's account of how a bill reached its current state.</summary>
public class InvoiceHistoryDto
{
    public string? Event { get; set; }
    public DateTimeOffset? Date { get; set; }
}
