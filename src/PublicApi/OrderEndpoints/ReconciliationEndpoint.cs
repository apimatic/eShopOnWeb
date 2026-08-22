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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<PayPalOnlyTransactionDto> PayPalOnly { get; set; } = new();
    public List<EShopOnlyPaymentDto> EShopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? MatchReason { get; set; }
}

public class PayPalOnlyTransactionDto
{
    public string? TransactionId { get; set; }
    public string? ReferenceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
}

public class EShopOnlyPaymentDto
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, checkout);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderCheckoutService checkout)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from)
            || !DateTimeOffset.TryParse(request.To, out var to))
        {
            return Results.BadRequest(new { message = "`from` and `to` must be ISO-8601 date-times." });
        }

        var report = await checkout.ReconcileAsync(from, to);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                OrderId = m.OrderId,
                PayPalTransactionId = m.PayPalTransactionId,
                MatchReason = m.MatchReason
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(t => new PayPalOnlyTransactionDto
            {
                TransactionId = t.TransactionId,
                ReferenceId = t.ReferenceId,
                EventCode = t.EventCode,
                Status = t.Status,
                InvoiceId = t.InvoiceId,
                CustomField = t.CustomField,
                InitiationDate = t.InitiationDate,
                Amount = t.AmountValue,
                Currency = t.AmountCurrency
            }).ToList(),
            EShopOnly = report.EShopOnly.Select(o => new EShopOnlyPaymentDto
            {
                OrderId = o.OrderId,
                PaymentStatus = o.PaymentStatus,
                PayPalOrderId = o.PayPalOrderId,
                AuthorizationId = o.AuthorizationId,
                CaptureId = o.CaptureId
            }).ToList()
        });
    }
}
