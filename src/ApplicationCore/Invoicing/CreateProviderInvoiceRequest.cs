using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>What eShop asks the provider to bill. Everything billed is derived from the order.</summary>
public record CreateProviderInvoiceRequest
{
    public string InvoiceNumber { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public DateOnly DueDate { get; init; }

    public decimal TotalAmount { get; init; }

    public string Currency { get; init; } = "USD";

    public string CustomerName { get; init; } = string.Empty;

    public string CustomerEmail { get; init; } = string.Empty;

    public IReadOnlyList<ProviderLineItem> LineItems { get; init; } = Array.Empty<ProviderLineItem>();
}

/// <summary>The correctable fields on a bill that has not yet been put to the shopper.</summary>
public record UpdateProviderInvoiceRequest
{
    public string Description { get; init; } = string.Empty;

    public DateOnly DueDate { get; init; }

    public decimal TotalAmount { get; init; }

    public string Currency { get; init; } = "USD";

    public string CustomerName { get; init; } = string.Empty;

    public string CustomerEmail { get; init; } = string.Empty;

    public IReadOnlyList<ProviderLineItem> LineItems { get; init; } = Array.Empty<ProviderLineItem>();
}

/// <summary>A single billed line, taken from an order item.</summary>
public record ProviderLineItem(string ProductSku, string ProductName, int Quantity, decimal UnitPrice);
