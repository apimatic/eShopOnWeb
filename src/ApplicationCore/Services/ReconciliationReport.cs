using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public enum ReconciliationMatchState
{
    Matched,
    PayPalOnly,
    EshopOnly
}

public class ReconciliationRow
{
    public string TransactionId { get; set; } = "";
    public string TransactionType { get; set; } = "";
    public string TransactionStatus { get; set; } = "";
    public DateTimeOffset TransactionDate { get; set; }
    public PayPalMoney? Amount { get; set; }
    public PayPalMoney? Fee { get; set; }
    public PayPalMoney? Net { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomId { get; set; }
    public int? OrderId { get; set; }
    public string? OrderStatus { get; set; }
    public ReconciliationMatchState MatchState { get; set; }
}

public class ReconciliationOrderRow
{
    public int OrderId { get; set; }
    public string Status { get; set; } = "";
    public decimal Total { get; set; }
    public string Currency { get; set; } = "";
    public DateTimeOffset OrderDate { get; set; }
    public string? PaymentStatus { get; set; }
}

public class ReconciliationReport
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationRow> Rows { get; set; } = new();
    public List<ReconciliationOrderRow> OrdersWithoutPayPalTransaction { get; set; } = new();
}
