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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderCheckoutService checkout) =>
            {
                return await HandleAsync(new ReconciliationQuery(from, to), checkout);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IOrderCheckoutService checkout)
    {
        var report = await checkout.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                OrderId = m.OrderId,
                PayPalTransactionId = m.PayPalTransactionId,
                InvoiceId = m.InvoiceId,
                MatchOn = m.MatchOn
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(t => new PayPalTransactionDto
            {
                TransactionId = t.TransactionId,
                ReferenceId = t.ReferenceId,
                ReferenceIdType = t.ReferenceIdType,
                EventCode = t.EventCode,
                Status = t.Status,
                InvoiceId = t.InvoiceId,
                CustomField = t.CustomField,
                Amount = t.Amount,
                Currency = t.Currency,
                InitiationDate = t.InitiationDate,
                FeeAmount = t.FeeAmount
            }).ToList(),
            EshopOnly = report.EshopOnly.Select(e => new EshopPaymentDto
            {
                OrderId = e.OrderId,
                Status = e.Status,
                PayPalOrderId = e.PayPalOrderId,
                PayPalAuthorizationId = e.PayPalAuthorizationId,
                PayPalCaptureId = e.PayPalCaptureId
            }).ToList()
        });
    }
}

public record ReconciliationQuery(DateTimeOffset From, DateTimeOffset To);

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationMatchDto> Matched { get; set; } = new();
    public System.Collections.Generic.List<PayPalTransactionDto> PayPalOnly { get; set; } = new();
    public System.Collections.Generic.List<EshopPaymentDto> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? InvoiceId { get; set; }
    public string MatchOn { get; set; } = string.Empty;
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
    public DateTimeOffset? InitiationDate { get; set; }
    public decimal? FeeAmount { get; set; }
}

public class EshopPaymentDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? PayPalAuthorizationId { get; set; }
    public string? PayPalCaptureId { get; set; }
}
