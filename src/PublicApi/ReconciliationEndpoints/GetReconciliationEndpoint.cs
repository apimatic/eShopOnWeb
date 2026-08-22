using System;
using System.Collections.Generic;
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

public class GetReconciliationEndpoint : IEndpoint<IResult, GetReconciliationRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, ICheckoutService checkout) =>
            {
                return await HandleAsync(new GetReconciliationRequest { From = from, To = to }, checkout);
            })
            .Produces<GetReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(GetReconciliationRequest request, ICheckoutService checkout)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from))
        {
            throw new CheckoutException(400, "Query parameter 'from' must be an ISO-8601 date-time.");
        }

        if (!DateTimeOffset.TryParse(request.To, out var to))
        {
            throw new CheckoutException(400, "Query parameter 'to' must be an ISO-8601 date-time.");
        }

        var report = await checkout.ReconcileAsync(from, to);
        return Results.Ok(new GetReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                OrderId = m.Payment.OrderId,
                EshopStatus = m.Payment.Status.ToString(),
                PayPalTransactionId = m.Transaction.TransactionId,
                PayPalStatus = m.Transaction.Status,
                Amount = m.Transaction.Amount,
                Currency = m.Transaction.Currency
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(t => new PayPalTransactionDto
            {
                TransactionId = t.TransactionId,
                ReferenceId = t.ReferenceId,
                CustomField = t.CustomField,
                InvoiceId = t.InvoiceId,
                EventCode = t.EventCode,
                Status = t.Status,
                Amount = t.Amount,
                Currency = t.Currency,
                InitiationDate = t.InitiationDate
            }).ToList(),
            EshopOnly = report.EshopOnly.Select(p => new EshopPaymentDto
            {
                OrderId = p.OrderId,
                Status = p.Status.ToString(),
                Amount = p.Amount,
                Currency = p.Currency,
                PayPalOrderId = p.PayPalOrderId,
                AuthorizationId = p.AuthorizationId,
                CaptureId = p.CaptureId
            }).ToList()
        });
    }
}

public class GetReconciliationRequest : BaseRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}

public class GetReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<PayPalTransactionDto> PayPalOnly { get; set; } = new();
    public List<EshopPaymentDto> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public int OrderId { get; set; }
    public string EshopStatus { get; set; } = string.Empty;
    public string PayPalTransactionId { get; set; } = string.Empty;
    public string? PayPalStatus { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
}

public class PayPalTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public string? CustomField { get; set; }
    public string? InvoiceId { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class EshopPaymentDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
}
