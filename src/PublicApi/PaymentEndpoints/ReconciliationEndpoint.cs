using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using BlazorShared.Authorization;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: lists PayPal's own transactions for a date range (across all pages)
/// and lines them up against eShop payment events, surfacing mismatches in both
/// directions: a payment PayPal knows about and eShop doesn't, or the reverse.
/// Note that PayPal's transaction reporting can lag live activity by up to a few hours,
/// so a range covering just-created payments may legitimately come back with no
/// PayPal-side records yet.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentService paymentService) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, paymentService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService paymentService)
    {
        if (request.From > request.To)
        {
            return Results.BadRequest(new { error = "validation_failed", message = "'from' must not be after 'to'." });
        }

        var result = await paymentService.ReconcileAsync(request.From, request.To);
        if (!result.Succeeded || result.Value == null)
        {
            return PaymentEndpointHelpers.FromError(result.Error!);
        }

        var report = result.Value;
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.MatchedCount,
            LastRefreshedDatetime = report.LastRefreshedDatetime,
            PayPalTransactions = report.PayPalTransactions.Select(t => new ReconciliationTransactionDto
            {
                TransactionId = t.TransactionId,
                EventCode = t.EventCode,
                Status = t.Status,
                Amount = t.Amount,
                Currency = t.Currency,
                FeeAmount = t.FeeAmount,
                InitiationDate = t.InitiationDate,
                InvoiceId = t.InvoiceId,
                ReferenceId = t.ReferenceId
            }).ToList(),
            ShopPayments = report.ShopPayments.Select(s => new ShopPaymentDto
            {
                OrderId = s.OrderId,
                PaymentKey = s.PaymentKey,
                Kind = s.Kind,
                PayPalId = s.PayPalId,
                Amount = s.Amount,
                Currency = s.Currency,
                Timestamp = s.Timestamp,
                Matched = s.Matched
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(t => new ReconciliationTransactionDto
            {
                TransactionId = t.TransactionId,
                EventCode = t.EventCode,
                Status = t.Status,
                Amount = t.Amount,
                Currency = t.Currency,
                FeeAmount = t.FeeAmount,
                InitiationDate = t.InitiationDate,
                InvoiceId = t.InvoiceId,
                ReferenceId = t.ReferenceId
            }).ToList(),
            ShopOnly = report.ShopOnly.Select(s => new ShopPaymentDto
            {
                OrderId = s.OrderId,
                PaymentKey = s.PaymentKey,
                Kind = s.Kind,
                PayPalId = s.PayPalId,
                Amount = s.Amount,
                Currency = s.Currency,
                Timestamp = s.Timestamp,
                Matched = s.Matched
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}





