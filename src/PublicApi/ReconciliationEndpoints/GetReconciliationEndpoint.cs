using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new GetReconciliationRequest { From = from, To = to }, paymentService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, IOrderPaymentService paymentService)
    {
        var from = ParseTimestamp(request.From, "from");
        var to = ParseTimestamp(request.To, "to");
        var report = await paymentService.ReconcileAsync(from, to);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matches = report.Matches.Select(m => new ReconciliationMatchResponse
            {
                OrderId = m.OrderId,
                PayPalTransactionId = m.PayPalTransactionId,
                MatchReason = m.MatchReason
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(ToTxn).ToList(),
            EshopOnly = report.EshopOnly.Select(ToEshop).ToList(),
            PayPalTransactions = report.PayPalTransactions.Select(ToTxn).ToList(),
            EshopPayments = report.EshopPayments.Select(ToEshop).ToList()
        });
    }

    private static DateTimeOffset ParseTimestamp(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new PaymentException(400, $"Query parameter '{name}' is required as an ISO-8601 date-time.");
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        throw new PaymentException(400, $"Query parameter '{name}' must be an ISO-8601 date-time.");
    }

    private static PayPalTransactionResponse ToTxn(PayPalReportedTransaction txn) =>
        new()
        {
            TransactionId = txn.TransactionId,
            PayPalReferenceId = txn.PayPalReferenceId,
            InvoiceId = txn.InvoiceId,
            CustomField = txn.CustomField,
            EventCode = txn.EventCode,
            Status = txn.Status,
            Amount = txn.Amount,
            FeeAmount = txn.FeeAmount,
            Currency = txn.Currency,
            InitiationDate = txn.InitiationDate
        };

    private static EshopPaymentResponse ToEshop(EshopPaymentRecord record) =>
        new()
        {
            OrderId = record.OrderId,
            Status = record.Status.ToString(),
            PayPalOrderId = record.PayPalOrderId,
            AuthorizationId = record.AuthorizationId,
            CaptureId = record.CaptureId,
            CapturedAmount = record.CapturedAmount,
            RefundIds = record.RefundIds.ToList()
        };
}

public class GetReconciliationRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationMatchResponse> Matches { get; set; } = new();
    public System.Collections.Generic.List<PayPalTransactionResponse> PayPalOnly { get; set; } = new();
    public System.Collections.Generic.List<EshopPaymentResponse> EshopOnly { get; set; } = new();
    public System.Collections.Generic.List<PayPalTransactionResponse> PayPalTransactions { get; set; } = new();
    public System.Collections.Generic.List<EshopPaymentResponse> EshopPayments { get; set; } = new();
}

public class ReconciliationMatchResponse
{
    public int OrderId { get; set; }
    public string PayPalTransactionId { get; set; } = string.Empty;
    public string MatchReason { get; set; } = string.Empty;
}

public class PayPalTransactionResponse
{
    public string TransactionId { get; set; } = string.Empty;
    public string? PayPalReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public decimal? FeeAmount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class EshopPaymentResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal? CapturedAmount { get; set; }
    public System.Collections.Generic.List<string> RefundIds { get; set; } = new();
}
