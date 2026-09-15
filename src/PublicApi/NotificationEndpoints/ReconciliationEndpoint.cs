using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: lists the provider's own record of messages from this application's configured
/// sending number over a date range and lines them up against what eShop believes it sent, so a message
/// either side knows about and the other does not is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderNotificationService notifications) =>
            {
                if (!TryParseIso(from, out var fromDate) || !TryParseIso(to, out var toDate))
                {
                    return Results.BadRequest("Both 'from' and 'to' must be supplied as ISO-8601 date-times.");
                }
                if (toDate < fromDate)
                {
                    return Results.BadRequest("'to' must not be earlier than 'from'.");
                }

                var report = await notifications.ReconcileAsync(fromDate, toDate);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal, out result);
    }

    public Task<IResult> HandleAsync(IOrderNotificationService notifications) =>
        Task.FromResult<IResult>(Results.Empty);
}
