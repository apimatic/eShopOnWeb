using System;
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

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<MatchedPaymentDto> Matched { get; set; } = new();
    public System.Collections.Generic.List<PayPalTransactionDto> PayPalOnly { get; set; } = new();
    public System.Collections.Generic.List<EShopPaymentDto> EShopOnly { get; set; } = new();
}

public class MatchedPaymentDto
{
    public int OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? InvoiceId { get; set; }
    public string MatchReason { get; set; } = string.Empty;
}

public class PayPalTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Fee { get; set; }
}

public class EShopPaymentDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? PayPalAuthorizationId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IPaymentReconciliationService reconciliation) =>
                await HandleAsync(new ReconciliationQuery(from, to), reconciliation))
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IPaymentReconciliationService reconciliation)
    {
        var report = await reconciliation.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(m => new MatchedPaymentDto
            {
                OrderId = m.OrderId,
                PayPalTransactionId = m.PayPalTransactionId,
                InvoiceId = m.InvoiceId,
                MatchReason = m.MatchReason
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(t => new PayPalTransactionDto
            {
                TransactionId = t.TransactionId,
                ReferenceId = t.ReferenceId,
                InvoiceId = t.InvoiceId,
                CustomField = t.CustomField,
                EventCode = t.EventCode,
                Status = t.Status,
                InitiationDate = t.InitiationDate,
                Amount = t.AmountValue,
                Currency = t.AmountCurrency,
                Fee = t.FeeValue
            }).ToList(),
            EShopOnly = report.EShopOnly.Select(e => new EShopPaymentDto
            {
                OrderId = e.OrderId,
                Status = e.Status,
                PayPalOrderId = e.PayPalOrderId,
                PayPalAuthorizationId = e.PayPalAuthorizationId,
                PayPalCaptureId = e.PayPalCaptureId,
                OrderDate = e.OrderDate
            }).ToList()
        });
    }
}

public readonly record struct ReconciliationQuery(DateTimeOffset From, DateTimeOffset To);
