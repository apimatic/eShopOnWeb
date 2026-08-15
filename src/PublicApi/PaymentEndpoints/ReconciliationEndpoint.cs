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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Date range for the reconciliation report.</summary>
public record ReconcileQuery(DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// Operator action (administrator). Lists PayPal's own record of transactions for a date range and
/// lines them up against eShop orders, surfacing mismatches in either direction. Covers the whole range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconcileQuery, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliationService) =>
                await HandleAsync(new ReconcileQuery(from, to), reconciliationService))
            .Produces<ReconciliationResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileQuery query, IReconciliationService reconciliationService)
    {
        var report = await reconciliationService.ReconcileAsync(query.From, query.To);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.Matched.Count,
            OnlyInPayPalCount = report.OnlyInPayPal.Count,
            OnlyInEShopCount = report.OnlyInEShop.Count,
            Matched = report.Matched.Select(PaymentMapping.ToLineDto).ToList(),
            OnlyInPayPal = report.OnlyInPayPal.Select(PaymentMapping.ToLineDto).ToList(),
            OnlyInEShop = report.OnlyInEShop.Select(PaymentMapping.ToLineDto).ToList()
        };
        return Results.Ok(response);
    }
}
