using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator report lining up PayPal's transaction
/// records against eShop orders across the whole range. from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService) =>
                await HandleAsync(new ReconciliationRequest { From = from, To = to }, reconciliationService))
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService reconciliationService)
    {
        var result = await reconciliationService.ReconcileAsync(request.From, request.To);
        return PaymentApi.ToHttp(result, report => new
        {
            from = report.From,
            to = report.To,
            payPalTransactionCount = report.PayPalTransactionCount,
            matchedCount = report.Matched.Count,
            payPalOnlyCount = report.InPayPalNotEShop.Count,
            eShopOnlyCount = report.InEShopNotPayPal.Count,
            matched = report.Matched,
            inPayPalNotEShop = report.InPayPalNotEShop,
            inEShopNotPayPal = report.InEShopNotPayPal
        });
    }
}
