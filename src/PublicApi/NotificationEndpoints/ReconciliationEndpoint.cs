using System;
using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Lists the provider's own record of messages for a date range and lines them up against what this
/// application believes it sent, so a message one side knows about and the other does not is visible.
/// It counts only messages sent from this application's own configured sending number. Administrator
/// role only. <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, ISmsNotificationService service) =>
            {
                if (!TryParseIso(from, out var fromDate))
                    return Results.BadRequest("Query parameter 'from' must be an ISO-8601 date-time.");
                if (!TryParseIso(to, out var toDate))
                    return Results.BadRequest("Query parameter 'to' must be an ISO-8601 date-time.");
                if (toDate < fromDate)
                    return Results.BadRequest("'to' must not be earlier than 'from'.");

                var report = await service.ReconcileAsync(fromDate, toDate);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
            out result);
    }
}
