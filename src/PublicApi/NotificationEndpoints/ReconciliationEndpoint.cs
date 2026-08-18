using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — lists the provider's own record of messages
/// sent from this application's configured number over the range and lines them up against what eShop believes
/// it sent, so any discrepancy in either direction is visible. from/to are ISO-8601 date-times. Restricted to
/// the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderNotificationService service) =>
                await HandleAsync(from, to, service))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service)
        => HandleAsync(null, null, service);

    private static async Task<IResult> HandleAsync(string? from, string? to, IOrderNotificationService service)
    {
        if (!TryParseIso(from, out var fromDt))
            return Results.BadRequest(new { message = "'from' must be an ISO-8601 date-time." });
        if (!TryParseIso(to, out var toDt))
            return Results.BadRequest(new { message = "'to' must be an ISO-8601 date-time." });
        if (toDt < fromDt)
            return Results.BadRequest(new { message = "'to' must be on or after 'from'." });

        var report = await service.ReconcileAsync(fromDt, toDt, CancellationToken.None);
        return Results.Ok(ReconciliationResponse.Create(report));
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
        {
            return true;
        }
        result = default;
        return false;
    }
}
