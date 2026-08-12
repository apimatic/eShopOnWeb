using System;
using System.Globalization;
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
/// Operator report: the provider's own record of messages from the application's configured sending
/// number over a date range, lined up against what eShop believes it sent, so a message one side knows
/// about and the other doesn't is visible. <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, string, string, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, HttpContext httpContext) =>
            {
                return await HandleAsync(from ?? string.Empty, to ?? string.Empty, httpContext);
            })
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(string from, string to, HttpContext httpContext)
    {
        if (!TryParse(from, out var fromValue))
            return Results.BadRequest(new { message = "'from' must be an ISO-8601 date-time." });
        if (!TryParse(to, out var toValue))
            return Results.BadRequest(new { message = "'to' must be an ISO-8601 date-time." });
        if (fromValue > toValue)
            return Results.BadRequest(new { message = "'from' must not be after 'to'." });

        var notificationService = httpContext.RequestServices.GetRequiredService<IOrderNotificationService>();
        var report = await notificationService.ReconcileAsync(fromValue, toValue, httpContext.RequestAborted);
        return Results.Ok(report);
    }

    private static bool TryParse(string value, out DateTimeOffset result)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal, out result);
}
