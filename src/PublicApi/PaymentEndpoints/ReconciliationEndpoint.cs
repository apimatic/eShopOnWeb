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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator action. Lists PayPal's own transactions for
/// the range and lines them up against eShop orders, surfacing either side's unmatched records. Covers the
/// whole range, not just the first page. from/to are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string? from, string? to, IPaymentService service, CancellationToken ct) =>
            {
                if (!TryParseIso(from, out var fromDt))
                    throw new PaymentStateException("Query parameter 'from' must be an ISO-8601 date-time.");
                if (!TryParseIso(to, out var toDt))
                    throw new PaymentStateException("Query parameter 'to' must be an ISO-8601 date-time.");

                var report = await service.ReconcileAsync(fromDt, toDt, ct);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("OrderPaymentEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out result))
        {
            return true;
        }
        result = default;
        return false;
    }
}
