using System;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// A lightweight entry from the provider's list of bills, with its creation date resolved so it can
/// be lined up against eShop's own record during reconciliation.
/// </summary>
public record ProviderInvoiceSummary
{
    public string Id { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset? CreatedDate { get; init; }

    public decimal? TotalAmount { get; init; }

    public string? Currency { get; init; }

    public string? CustomerName { get; init; }

    public DateOnly? DueDate { get; init; }
}
