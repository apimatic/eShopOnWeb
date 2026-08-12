using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — lists the provider's own record of
/// messages sent from this application's configured sending number in the range and lines them up
/// against what eShop believes it sent, so a message one side knows about and the other doesn't is
/// visible. <c>from</c>/<c>to</c> are ISO-8601 date-times. Operator-only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, string, string, HttpContext>
{
    private readonly IOrderNotificationService _orderNotifications;

    public ReconciliationEndpoint(IOrderNotificationService orderNotifications)
    {
        _orderNotifications = orderNotifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, HttpContext http) =>
            {
                return await HandleAsync(from, to, http);
            })
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(string? from, string? to, HttpContext http)
    {
        if (!TryParseIso(from, out var fromValue))
        {
            return Results.BadRequest(new { error = "Query parameter 'from' is required and must be an ISO-8601 date-time." });
        }
        if (!TryParseIso(to, out var toValue))
        {
            return Results.BadRequest(new { error = "Query parameter 'to' is required and must be an ISO-8601 date-time." });
        }
        if (fromValue > toValue)
        {
            return Results.BadRequest(new { error = "'from' must not be later than 'to'." });
        }

        try
        {
            var report = await _orderNotifications.ReconcileAsync(fromValue, toValue, http.RequestAborted);
            return Results.Ok(report);
        }
        catch (TwilioApiException ex)
        {
            return Results.Problem(
                title: "The provider's message record could not be retrieved.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
