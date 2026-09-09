using System;
using System.Threading;
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
/// GET /api/reconciliation?from={from}&amp;to={to} — operator action. Lists PayPal's own record of
/// transactions for a date range and lines them up against eShop orders. Covers the whole range.
/// Restricted to the administrator role. Note: PayPal's reporting lags live activity, so a range
/// covering payments just created may legitimately come back empty.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                IReconciliationService service,
                CancellationToken ct) =>
            {
                if (from is null || to is null)
                {
                    return Results.BadRequest(new { message = "Both 'from' and 'to' ISO-8601 date-times are required." });
                }
                if (to <= from)
                {
                    return Results.BadRequest(new { message = "'to' must be after 'from'." });
                }

                try
                {
                    var report = await service.ReconcileAsync(from.Value, to.Value, ct);
                    return Results.Ok(report);
                }
                catch (Exception ex)
                {
                    return PaymentProblems.ToResult(ex);
                }
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderPaymentEndpoints");
    }
}
