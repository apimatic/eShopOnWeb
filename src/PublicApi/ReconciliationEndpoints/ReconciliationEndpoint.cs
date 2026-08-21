using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOrderPaymentService orders, HttpContext httpContext) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromDate)
                    || !DateTimeOffset.TryParse(to, out var toDate))
                {
                    throw new PaymentException(400, "`from` and `to` must be ISO-8601 date-times.");
                }

                var report = await orders.ReconcileAsync(fromDate, toDate, httpContext.RequestAborted);
                return Results.Ok(new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    Matches = report.Matches.Select(m => new ReconciliationMatchDto
                    {
                        OrderId = m.OrderId,
                        PayPalTransactionId = m.PayPalTransactionId,
                        MatchReason = m.MatchReason
                    }).ToList(),
                    PayPalOnly = report.PayPalOnly.Select(t => new PayPalTransactionDto
                    {
                        TransactionId = t.TransactionId,
                        Status = t.Status,
                        Amount = t.Amount,
                        Currency = t.Currency,
                        FeeAmount = t.FeeAmount,
                        InitiationDate = t.InitiationDate,
                        InvoiceId = t.InvoiceId,
                        CustomField = t.CustomField,
                        PaypalReferenceId = t.PaypalReferenceId,
                        PaypalReferenceIdType = t.PaypalReferenceIdType
                    }).ToList(),
                    EShopOnly = report.EShopOnly.Select(o => new EShopOrderDto
                    {
                        OrderId = o.OrderId,
                        Status = o.Status.ToString(),
                        PayPalOrderId = o.PayPalOrderId,
                        AuthorizationId = o.AuthorizationId,
                        CaptureId = o.CaptureId,
                        Total = o.Total
                    }).ToList()
                });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderPaymentService orders) => Task.FromResult(Results.BadRequest());
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public System.Collections.Generic.List<ReconciliationMatchDto> Matches { get; set; } = new();
    public System.Collections.Generic.List<PayPalTransactionDto> PayPalOnly { get; set; } = new();
    public System.Collections.Generic.List<EShopOrderDto> EShopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string MatchReason { get; set; } = string.Empty;
}

public class PayPalTransactionDto
{
    public string? TransactionId { get; set; }
    public string? Status { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? FeeAmount { get; set; }
    public string? InitiationDate { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? PaypalReferenceIdType { get; set; }
}

public class EShopOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public decimal Total { get; set; }
}
