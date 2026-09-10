using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

/// <summary>
/// Operator report: PayPal's own record of transactions for a date range, lined up against eShop orders so
/// a payment one side knows and the other doesn't is visible. Covers the whole range (every page).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to, Ct = ct }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("PayPalPayments");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To, request.Ct);
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            PayPalTransactionCount = report.PayPalTransactionCount,
            MatchedCount = report.MatchedCount,
            Matched = report.Matched
                .Select(m => new ReconciliationMatchDto(m.OrderId, m.EShopStatus.ToString(), m.EShopAmount,
                    PaymentMapper.ToDto(m.PayPalTransaction)))
                .ToList(),
            InPayPalOnly = report.InPayPalOnly.Select(PaymentMapper.ToDto).ToList(),
            InEShopOnly = report.InEShopOnly
                .Select(o => new ReconciliationOrderDto(o.OrderId, o.Status.ToString(), o.Amount, o.Currency, o.CaptureId))
                .ToList(),
        };
        return Results.Ok(response);
    }
}
