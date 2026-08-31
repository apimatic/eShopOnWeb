using System;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// A bill as eShop sees it. <see cref="InvoiceId"/> is the identifier the operator endpoints act on.
/// <see cref="Status"/> is eShop's authoritative lifecycle; <see cref="ProviderStatus"/> is the provider's
/// own free-form status string.
/// </summary>
public class InvoiceDto
{
    public int InvoiceId { get; set; }
    public int OrderId { get; set; }
    public string ProviderInvoiceId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset DueDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public DateTimeOffset CreatedDate { get; set; }
}
