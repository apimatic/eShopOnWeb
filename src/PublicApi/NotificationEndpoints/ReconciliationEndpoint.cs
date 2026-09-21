using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: reports the provider's own record of this application's messages for a date range and
/// lines them up against what eShop believes it sent, so a message the provider knows about and eShop
/// doesn't — or the reverse — is visible. Counts only messages sent from this application's configured
/// sending number, over the whole range. <c>from</c>/<c>to</c> are ISO-8601 date-times. Admin only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            // ClaimsPrincipal param keeps this off the RequestDelegate overload.
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, System.Security.Claims.ClaimsPrincipal user) => await HandleAsync(http))
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var fromRaw = http.Request.Query["from"].ToString();
        var toRaw = http.Request.Query["to"].ToString();

        if (!TryParseIso(fromRaw, out var from))
            return Results.BadRequest(new { message = "Query parameter 'from' must be an ISO-8601 date-time." });
        if (!TryParseIso(toRaw, out var to))
            return Results.BadRequest(new { message = "Query parameter 'to' must be an ISO-8601 date-time." });
        if (from > to)
            return Results.BadRequest(new { message = "'from' must not be after 'to'." });

        var notifier = http.RequestServices.GetRequiredService<IOrderNotificationService>();
        try
        {
            var report = await notifier.ReconcileAsync(from, to, http.RequestAborted);
            return Results.Ok(report);
        }
        catch (SmsProviderException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static bool TryParseIso(string value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
}
