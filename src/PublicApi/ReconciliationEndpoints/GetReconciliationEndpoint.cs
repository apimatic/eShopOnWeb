using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator action: lists PayPal's own record of transactions for a date range and
/// lines them up against eShop orders, so a payment PayPal knows about and eShop
/// doesn't - or the reverse - is visible. Covers the whole range, not just the first
/// page. from/to are ISO-8601 date-times.
/// </summary>
public class GetReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService) =>
            {
                return await HandleAsync(from, to, reconciliationService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService)
    {
        if (to <= from)
        {
            return Results.BadRequest(new { message = "The 'to' date-time must be after the 'from' date-time." });
        }

        var report = await reconciliationService.GetReportAsync(from, to);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            TotalPayPalTransactions = report.TotalPayPalTransactions,
            MatchedTransactions = report.MatchedTransactions,
            UnmatchedPayPalTransactions = report.TotalPayPalTransactions - report.MatchedTransactions
        };

        foreach (var tx in report.Transactions)
        {
            response.Transactions.Add(new ReconciliationTransactionDto
            {
                TransactionId = tx.TransactionId,
                ReferenceId = tx.ReferenceId,
                EventCode = tx.EventCode,
                Status = tx.Status,
                Amount = tx.Amount,
                Currency = tx.Currency,
                FeeAmount = tx.FeeAmount,
                InvoiceId = tx.InvoiceId,
                InitiationDate = tx.InitiationDate,
                MatchedToEshopOrder = tx.MatchedToEshopOrder,
                EshopOrderId = tx.EshopOrderId,
                MatchType = tx.MatchType
            });
        }

        foreach (var payment in report.EshopPaymentsNotInPayPalReport)
        {
            response.EshopPaymentsNotInPayPalReport.Add(new ReconciliationLocalPaymentDto
            {
                OrderId = payment.OrderId,
                BuyerId = payment.BuyerId,
                Status = payment.Status,
                AuthorizationId = payment.AuthorizationId,
                CaptureId = payment.CaptureId,
                CapturedAmount = payment.CapturedAmount,
                Currency = payment.Currency
            });
        }

        return Results.Ok(response);
    }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int TotalPayPalTransactions { get; set; }
    public int MatchedTransactions { get; set; }
    public int UnmatchedPayPalTransactions { get; set; }
    public List<ReconciliationTransactionDto> Transactions { get; set; } = new List<ReconciliationTransactionDto>();
    public List<ReconciliationLocalPaymentDto> EshopPaymentsNotInPayPalReport { get; set; } = new List<ReconciliationLocalPaymentDto>();
}

public class ReconciliationTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? FeeAmount { get; set; }
    public string? InvoiceId { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public bool MatchedToEshopOrder { get; set; }
    public int? EshopOrderId { get; set; }
    public string? MatchType { get; set; }
}

public class ReconciliationLocalPaymentDto
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
