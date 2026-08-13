using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: a report lining up the provider's own record of messages sent from this
/// application's configured number, over a date range, against what eShop believes it sent — so a
/// message one side knows about and the other does not is visible. Restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, HttpContext http) => await HandleAsync(http))
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        if (!TryParseIso(http.Request.Query["from"], out var from))
            return Results.BadRequest(new { error = "Query parameter 'from' must be an ISO-8601 date-time." });
        if (!TryParseIso(http.Request.Query["to"], out var to))
            return Results.BadRequest(new { error = "Query parameter 'to' must be an ISO-8601 date-time." });
        if (from > to)
            return Results.BadRequest(new { error = "'from' must not be after 'to'." });

        var service = http.RequestServices.GetRequiredService<IOrderNotificationService>();
        var report = await service.ReconcileAsync(from, to, http.RequestAborted);
        return Results.Ok(report);
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
