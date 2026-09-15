using System;
using System.Collections.Generic;
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

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IPaymentReconciliationService reconciliation) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), reconciliation);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentReconciliationService reconciliation)
    {
        var from = ParseRequired(request.From, "from");
        var to = ParseRequired(request.To, "to");
        var report = await reconciliation.ReconcileAsync(from, to);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            PayPalTransactions = report.PayPalTransactions.Select(MapTransaction).ToList(),
            Matches = report.Matches.Select(m => new ReconciliationMatchDto
            {
                OrderId = m.OrderId,
                Transaction = MapTransaction(m.Transaction)
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(MapTransaction).ToList(),
            EshopOnly = report.EshopOnly.Select(e => new EshopPaymentRecordDto
            {
                OrderId = e.OrderId,
                Status = e.Status,
                PayPalOrderId = e.PayPalOrderId,
                AuthorizationId = e.AuthorizationId,
                CaptureId = e.CaptureId,
                RefundIds = e.RefundIds.ToList()
            }).ToList()
        });
    }

    private static DateTimeOffset ParseRequired(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new PaymentException($"Query parameter `{name}` is required and must be an ISO-8601 date-time.");
        }

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new PaymentException($"Query parameter `{name}` must be an ISO-8601 date-time.");
        }

        return parsed;
    }

    private static PayPalTransactionDto MapTransaction(ApplicationCore.Payment.PayPalReportedTransaction transaction)
    {
        return new PayPalTransactionDto
        {
            TransactionId = transaction.TransactionId,
            ReferenceId = transaction.ReferenceId,
            ReferenceIdType = transaction.ReferenceIdType,
            EventCode = transaction.EventCode,
            Status = transaction.Status,
            InvoiceId = transaction.InvoiceId,
            CustomField = transaction.CustomField,
            Amount = transaction.Amount?.Value,
            Currency = transaction.Amount?.Currency,
            FeeAmount = transaction.FeeAmount?.Value,
            InitiationDate = transaction.InitiationDate
        };
    }
}

public class ReconciliationRequest
{
    public ReconciliationRequest(string? from, string? to)
    {
        From = from;
        To = to;
    }

    public string? From { get; }
    public string? To { get; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<PayPalTransactionDto> PayPalTransactions { get; set; } = new();
    public List<ReconciliationMatchDto> Matches { get; set; } = new();
    public List<PayPalTransactionDto> PayPalOnly { get; set; } = new();
    public List<EshopPaymentRecordDto> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public PayPalTransactionDto Transaction { get; set; } = new();
}

public class PayPalTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? ReferenceIdType { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class EshopPaymentRecordDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public List<string> RefundIds { get; set; } = new();
}
