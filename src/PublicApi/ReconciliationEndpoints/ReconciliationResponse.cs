using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>PayPal's record of transactions for a range, lined up against this application's payments.</summary>
public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public DateTimeOffset Generated { get; set; }
    public string Currency { get; set; } = string.Empty;
    public ReconciliationSummaryDto Summary { get; set; } = new ReconciliationSummaryDto();
    public List<ReconciliationTransactionDto> PayPalTransactions { get; set; } = new List<ReconciliationTransactionDto>();
    public List<ReconciliationPaymentDto> EshopPayments { get; set; } = new List<ReconciliationPaymentDto>();

    public static ReconciliationResponse Build(ReconciliationReport report, Guid correlationId)
    {
        var response = new ReconciliationResponse(correlationId)
        {
            From = report.From,
            To = report.To,
            Generated = report.Generated,
            Currency = report.Currency,
            Summary = new ReconciliationSummaryDto
            {
                PayPalTransactionCount = report.Summary.PayPalTransactionCount,
                EshopPaymentCount = report.Summary.EshopPaymentCount,
                MatchedCount = report.Summary.MatchedCount,
                OnlyInPayPalCount = report.Summary.OnlyInPayPalCount,
                OnlyInEshopCount = report.Summary.OnlyInEshopCount,
                PayPalGrossAmount = report.Summary.PayPalGrossAmount,
                PayPalFeesAmount = report.Summary.PayPalFeesAmount,
                EshopCapturedAmount = report.Summary.EshopCapturedAmount,
                EshopRefundedAmount = report.Summary.EshopRefundedAmount
            }
        };

        foreach (var line in report.PayPalTransactions)
        {
            response.PayPalTransactions.Add(new ReconciliationTransactionDto
            {
                TransactionId = line.Transaction.TransactionId,
                ReferenceId = line.Transaction.ReferenceId,
                TransactionType = Classify(line),
                EventCode = line.Transaction.EventCode,
                Status = line.Transaction.Status,
                Amount = line.Transaction.Amount,
                Currency = line.Transaction.Currency,
                FeeAmount = line.Transaction.FeeAmount,
                InvoiceId = line.Transaction.InvoiceId,
                CustomField = line.Transaction.CustomField,
                TransactionDate = line.Transaction.TransactionDate,
                KnownToEshop = line.KnownToEshop,
                EshopOrderId = line.EshopOrderId,
                EshopPaymentId = line.EshopPaymentId
            });
        }

        foreach (var payment in report.EshopPayments)
        {
            response.EshopPayments.Add(ReconciliationPaymentDto.From(payment));
        }

        return response;
    }

    /// <summary>
    /// PayPal's statement rows carry an event code rather than a type; the codes seen from the sandbox
    /// are mapped here and anything else is reported as its raw code.
    /// </summary>
    private static string Classify(ReconciliationLine line)
    {
        if (!line.KnownToEshop)
        {
            return "UNATTRIBUTED";
        }

        return line.Transaction.EventCode switch
        {
            "T1300" => "AUTHORIZATION",
            "T0005" => "CAPTURE",
            "T1107" => "REFUND",
            "T0001" => "PAYMENT",
            var code when !string.IsNullOrEmpty(code) => code,
            _ => "TRANSACTION"
        };
    }
}

public class ReconciliationSummaryDto
{
    public int PayPalTransactionCount { get; set; }
    public int EshopPaymentCount { get; set; }
    public int MatchedCount { get; set; }
    public int OnlyInPayPalCount { get; set; }
    public int OnlyInEshopCount { get; set; }
    public decimal PayPalGrossAmount { get; set; }
    public decimal PayPalFeesAmount { get; set; }
    public decimal EshopCapturedAmount { get; set; }
    public decimal EshopRefundedAmount { get; set; }
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal? FeeAmount { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public DateTimeOffset TransactionDate { get; set; }
    public bool KnownToEshop { get; set; }
    public int? EshopOrderId { get; set; }
    public int? EshopPaymentId { get; set; }
}

public class ReconciliationPaymentDto
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal AuthorizedAmount { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal FeeAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public decimal RefundableAmount { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public List<string> RefundIds { get; set; } = new List<string>();
    public bool SeenInPayPalRecord { get; set; }
    public List<string> Issues { get; set; } = new List<string>();

    public static ReconciliationPaymentDto From(ReconciliationPayment payment) => new()
    {
        PaymentId = payment.PaymentId,
        OrderId = payment.OrderId,
        PaymentStatus = payment.PaymentStatus,
        AuthorizedAmount = payment.AuthorizedAmount,
        CapturedAmount = payment.CapturedAmount,
        FeeAmount = payment.FeeAmount,
        NetAmount = payment.NetAmount,
        RefundedAmount = payment.RefundedAmount,
        RefundableAmount = payment.RefundableAmount,
        PayPalOrderId = payment.PayPalOrderId,
        AuthorizationId = payment.AuthorizationId,
        CaptureId = payment.CaptureId,
        RefundIds = payment.RefundIds.ToList(),
        SeenInPayPalRecord = payment.SeenInPayPalRecord,
        Issues = payment.Issues.ToList()
    };
}
