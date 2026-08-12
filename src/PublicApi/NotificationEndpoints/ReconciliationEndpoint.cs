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
using Microsoft.eShopWeb.Infrastructure.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: lists the provider's own record of messages sent from this application's
/// configured sending number for a date-time range and lines them up against what eShop believes it
/// sent. Restricted to administrators. <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, INotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(from, to, notificationService, cancellationToken);
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(string? from, string? to, INotificationService notificationService,
        CancellationToken cancellationToken)
    {
        if (!TryParseIso(from, out var fromDate) || !TryParseIso(to, out var toDate))
        {
            return Results.BadRequest(new { message = "'from' and 'to' must be ISO-8601 date-times (e.g. 2026-08-12T00:00:00Z)." });
        }

        if (fromDate > toDate)
        {
            return Results.BadRequest(new { message = "'from' must not be after 'to'." });
        }

        try
        {
            var report = await notificationService.ReconcileAsync(fromDate, toDate, cancellationToken);
            return Results.Ok(report);
        }
        catch (TwilioApiException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "The provider could not be queried for reconciliation.", detail: ex.SafeSummary);
        }
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
