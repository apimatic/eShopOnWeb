using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IPaymentCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IPaymentCheckoutService payments) =>
            {
                return await HandleAsync(new ReconciliationQuery(from, to), payments);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IPaymentCheckoutService payments)
    {
        var report = await payments.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matches = report.Matches.Select(m => new ReconciliationMatchResponse
            {
                OrderId = m.OrderId,
                PayPalTransactionId = m.PayPalTransaction.TransactionId,
                ReferenceId = m.PayPalTransaction.ReferenceId,
                InvoiceId = m.PayPalTransaction.InvoiceId,
                CustomField = m.PayPalTransaction.CustomField,
                EventCode = m.PayPalTransaction.EventCode,
                Status = m.PayPalTransaction.Status,
                Amount = m.PayPalTransaction.Amount,
                Currency = m.PayPalTransaction.Currency,
                Fee = m.PayPalTransaction.Fee,
                InitiationDate = m.PayPalTransaction.InitiationDate
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(t => new PayPalOnlyTransactionResponse
            {
                PayPalTransactionId = t.TransactionId,
                ReferenceId = t.ReferenceId,
                InvoiceId = t.InvoiceId,
                CustomField = t.CustomField,
                EventCode = t.EventCode,
                Status = t.Status,
                Amount = t.Amount,
                Currency = t.Currency,
                Fee = t.Fee,
                InitiationDate = t.InitiationDate
            }).ToList(),
            EshopOnly = report.EshopOnly.Select(e => new EshopOnlyOrderResponse
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
}

public class ReconciliationQuery
{
    public ReconciliationQuery(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMatchResponse> Matches { get; set; } = new();
    public List<PayPalOnlyTransactionResponse> PayPalOnly { get; set; } = new();
    public List<EshopOnlyOrderResponse> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchResponse
{
    public int OrderId { get; set; }
    public string PayPalTransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class PayPalOnlyTransactionResponse
{
    public string PayPalTransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? Fee { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class EshopOnlyOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public List<string> RefundIds { get; set; } = new();
}
