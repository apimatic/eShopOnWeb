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

/// <summary>
/// Operator report lining PayPal's own transactions up against eShop orders over a date range.
/// Restricted to administrators. GET /api/reconciliation?from={from}&amp;to={to}
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private readonly IReconciliationService _service;

    public ReconciliationEndpoint(IReconciliationService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) => await HandleAsync(from, to))
            .Produces<ReconciliationResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var report = await _service.ReconcileAsync(from, to);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.Matched.Count,
            InPayPalNotEShopCount = report.InPayPalNotEShop.Count,
            InEShopNotPayPalCount = report.InEShopNotPayPal.Count,
            Matched = report.Matched.Select(e => e.ToDto()).ToList(),
            InPayPalNotEShop = report.InPayPalNotEShop.Select(e => e.ToDto()).ToList(),
            InEShopNotPayPal = report.InEShopNotPayPal.Select(e => e.ToDto()).ToList()
        };
        return Results.Ok(response);
    }
}
