using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>
/// The information eShop hands to the invoicing provider when it raises or corrects a bill.
/// It is derived entirely from the order plus the correctable customer/due-date details;
/// the caller never restates the amount.
/// </summary>
public class ProviderInvoiceDraft
{
    public ProviderInvoiceDraft(string description,
        DateOnly dueDate,
        string currencyCode,
        decimal totalAmount,
        string? customerName,
        string? customerEmail,
        IReadOnlyList<ProviderInvoiceLineItem> lineItems)
    {
        Description = description;
        DueDate = dueDate;
        CurrencyCode = currencyCode;
        TotalAmount = totalAmount;
        CustomerName = customerName;
        CustomerEmail = customerEmail;
        LineItems = lineItems;
    }

    public string Description { get; }
    public DateOnly DueDate { get; }
    public string CurrencyCode { get; }
    public decimal TotalAmount { get; }
    public string? CustomerName { get; }
    public string? CustomerEmail { get; }
    public IReadOnlyList<ProviderInvoiceLineItem> LineItems { get; }
}

public class ProviderInvoiceLineItem
{
    public ProviderInvoiceLineItem(string productName, string? productSku, int quantity, decimal unitPrice, decimal totalAmount)
    {
        ProductName = productName;
        ProductSku = productSku;
        Quantity = quantity;
        UnitPrice = unitPrice;
        TotalAmount = totalAmount;
    }

    public string ProductName { get; }
    public string? ProductSku { get; }
    public int Quantity { get; }
    public decimal UnitPrice { get; }
    public decimal TotalAmount { get; }
}
