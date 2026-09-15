using System;
using System.Globalization;
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
/// GET /api/reconciliation?from={from}&amp;to={to} — operator report lining PayPal's own transaction
/// records for the date range up against eShop orders. Restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IReconciliationService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationQuery(from, to, ct), service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("Reconciliation");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery query, IReconciliationService service)
    {
        if (!TryParseIso(query.From, out var from) || !TryParseIso(query.To, out var to))
        {
            return Results.BadRequest("'from' and 'to' must be ISO-8601 date-times.");
        }

        if (to < from)
        {
            return Results.BadRequest("'to' must not be earlier than 'from'.");
        }

        var report = await service.ReconcileAsync(from, to, query.Ct);
        return Results.Ok(PaymentApiMapper.ToReconciliationResponse(report));
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out result);
    }
}

public record ReconciliationQuery(string? From, string? To, CancellationToken Ct);
