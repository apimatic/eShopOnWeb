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

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }

    public ReconciliationReport Report { get; set; } = null!;
}

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator report listing PayPal's own record of
/// transactions over an ISO-8601 date-time range (paged over the whole range) and lining it up against
/// eShop orders. Administrator-only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string? from,
                string? to,
                IReconciliationService service,
                CancellationToken ct) =>
            {
                var fromDate = ParseIso(from, nameof(from));
                var toDate = ParseIso(to, nameof(to));
                if (toDate < fromDate)
                {
                    throw new OrderValidationException("'to' must be on or after 'from'.");
                }

                var report = await service.BuildReportAsync(fromDate, toDate, ct);
                var response = new ReconciliationResponse(Guid.NewGuid()) { Report = report };
                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("PaymentEndpoints");
    }

    private static DateTimeOffset ParseIso(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            throw new OrderValidationException($"'{name}' must be an ISO-8601 date-time (e.g. 2026-08-01T00:00:00Z).");
        }
        return parsed;
    }
}
