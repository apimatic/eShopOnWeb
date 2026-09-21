using System;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — lists PayPal's own transactions for a date range and
/// lines them up against eShop orders. Covers the whole range (chunked into ≤31-day windows, fully paged).
/// Restricted to the administrator role. <c>from</c>/<c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, HttpContext http, IPaymentService payments) =>
            {
                if (to < from)
                {
                    return Results.Json(
                        new { statusCode = StatusCodes.Status400BadRequest, message = "'to' must be on or after 'from'." },
                        statusCode: StatusCodes.Status400BadRequest);
                }
                try
                {
                    using var cts = PaymentEndpointSupport.CreateBudget(http);
                    var result = await payments.ReconcileAsync(from, to, cts.Token);
                    return Results.Ok(result);
                }
                catch (Exception ex)
                {
                    return PaymentEndpointSupport.ToResult(ex);
                }
            })
            .Produces<ReconciliationReport>()
            .WithTags("PaymentEndpoints");
    }
}
