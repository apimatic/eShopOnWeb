using System;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

// A single row from PayPal's own Transaction Search report.
public class PayPalTransactionRecord
{
    public PayPalTransactionRecord(string transactionId, decimal amount, string currencyCode, DateTimeOffset initiationDate, string status, string eventCode)
    {
        TransactionId = transactionId;
        Amount = amount;
        CurrencyCode = currencyCode;
        InitiationDate = initiationDate;
        Status = status;
        EventCode = eventCode;
    }

    public string TransactionId { get; }
    public decimal Amount { get; }
    public string CurrencyCode { get; }
    public DateTimeOffset InitiationDate { get; }
    public string Status { get; }
    public string EventCode { get; }
}
