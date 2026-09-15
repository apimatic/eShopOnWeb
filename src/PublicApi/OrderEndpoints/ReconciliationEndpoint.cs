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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationApiRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, ICheckoutService checkout) =>
            {
                return await HandleAsync(new ReconciliationApiRequest { From = from, To = to }, checkout);
            })
            .Produces<ReconciliationApiResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationApiRequest request, ICheckoutService checkout)
    {
        var report = await checkout.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationApiResponse
        {
            From = report.From,
            To = report.To,
            Matches = report.Matches.Select(m => new ReconciliationMatchResponse
            {
                OrderId = m.OrderId,
                PayPalTransactionId = m.PayPalTransaction.TransactionId,
                PayPalReferenceId = m.PayPalTransaction.PayPalReferenceId,
                InvoiceId = m.PayPalTransaction.InvoiceId,
                CustomField = m.PayPalTransaction.CustomField,
                PayPalStatus = m.PayPalTransaction.Status,
                PayPalAmount = m.PayPalTransaction.Amount,
                EshopCaptureId = m.EshopPayment.CaptureId,
                EshopAuthorizationId = m.EshopPayment.AuthorizationId
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(t => new PayPalTransactionResponse
            {
                TransactionId = t.TransactionId,
                PayPalReferenceId = t.PayPalReferenceId,
                InvoiceId = t.InvoiceId,
                CustomField = t.CustomField,
                EventCode = t.EventCode,
                Status = t.Status,
                Amount = t.Amount,
                Currency = t.Currency,
                FeeAmount = t.FeeAmount,
                InitiationDate = t.InitiationDate
            }).ToList(),
            EshopOnly = report.EshopOnly.Select(p => new EshopUnmatchedPaymentResponse
            {
                OrderId = p.OrderId,
                PayPalOrderId = p.PayPalOrderId,
                AuthorizationId = p.AuthorizationId,
                CaptureId = p.CaptureId,
                AuthorizedAt = p.OriginalAuthorizedAt,
                CapturedAmount = p.CapturedAmount
            }).ToList()
        });
    }
}

public class ReconciliationApiRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationApiResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMatchResponse> Matches { get; set; } = new();
    public List<PayPalTransactionResponse> PayPalOnly { get; set; } = new();
    public List<EshopUnmatchedPaymentResponse> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchResponse
{
    public int OrderId { get; set; }
    public string? PayPalTransactionId { get; set; }
    public string? PayPalReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? PayPalStatus { get; set; }
    public string? PayPalAmount { get; set; }
    public string? EshopCaptureId { get; set; }
    public string? EshopAuthorizationId { get; set; }
}

public class PayPalTransactionResponse
{
    public string? TransactionId { get; set; }
    public string? PayPalReferenceId { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public string? EventCode { get; set; }
    public string? Status { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public string? FeeAmount { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
}

public class EshopUnmatchedPaymentResponse
{
    public int OrderId { get; set; }
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public DateTimeOffset? AuthorizedAt { get; set; }
    public decimal? CapturedAmount { get; set; }
}
