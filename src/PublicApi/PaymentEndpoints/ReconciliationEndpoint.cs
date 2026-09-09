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

/// <summary>
/// GET /api/reconciliation?from=&amp;to= — operator action. Lists PayPal's own transaction records
/// for a date range and lines them up against eShop orders, surfacing records known to only one side.
/// Covers the whole range (chunked + fully paged), not just the first page.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService service) =>
            {
                var report = await service.ReconcileAsync(from, to);
                return Results.Ok(new
                {
                    from = report.From,
                    to = report.To,
                    summary = new
                    {
                        matched = report.Matched.Count,
                        payPalOnly = report.PayPalOnly.Count,
                        eShopOnly = report.EShopOnly.Count
                    },
                    matched = report.Matched,
                    payPalOnly = report.PayPalOnly,
                    eShopOnly = report.EShopOnly
                });
            })
            .WithTags("OrderPaymentEndpoints");
    }
}
