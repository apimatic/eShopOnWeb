using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments.OrderEndpoints;

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator report lining PayPal's own record of
/// transactions up against eShop orders over a date range (whole range, all pages).
/// Restricted to administrators.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderPaymentService service, CancellationToken ct) =>
            {
                if (!TryParseIso(from, out var fromDate))
                    throw new PaymentException("Query parameter 'from' must be an ISO-8601 date-time.");
                if (!TryParseIso(to, out var toDate))
                    throw new PaymentException("Query parameter 'to' must be an ISO-8601 date-time.");

                var report = await service.ReconcileAsync(fromDate, toDate, ct);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("OrderPaymentEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
