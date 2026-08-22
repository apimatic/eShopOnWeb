using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, ICheckoutPaymentService checkout) =>
            {
                var report = await checkout.ReconcileAsync(from, to);
                return Results.Ok(new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    Matches = report.Matches.Select(m => new ReconciliationMatchDto
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
                        Amount = t.Amount,
                        Currency = t.Currency,
                        FeeAmount = t.FeeAmount
                    }).ToList(),
                    EshopOnly = report.EshopOnly.Select(e => new EshopOnlyDto
                    {
                        OrderId = e.OrderId,
                        Status = e.Status,
                        PayPalOrderId = e.PayPalOrderId,
                        AuthorizationId = e.AuthorizationId,
                        CaptureId = e.CaptureId,
                        RefundIds = e.RefundIds.ToList()
                    }).ToList()
                });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(DateTimeOffset request, ICheckoutPaymentService checkout) =>
        Task.FromResult(Results.BadRequest());
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationMatchDto> Matches { get; set; } = new();
    public System.Collections.Generic.List<PayPalTransactionDto> PayPalOnly { get; set; } = new();
    public System.Collections.Generic.List<EshopOnlyDto> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? InvoiceId { get; set; }
    public string? MatchReason { get; set; }
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
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
}

public class EshopOnlyDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public System.Collections.Generic.List<string> RefundIds { get; set; } = new();
}
