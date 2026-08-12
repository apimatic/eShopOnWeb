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

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: the provider's own record of messages sent from the configured sending number
/// over a date range, lined up against what eShop believes it sent — so a message the provider knows
/// about and eShop doesn't (or the reverse) is visible. The whole range is covered.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationEndpoint.Request, IOrderNotificationService>
{
    public record Request(DateTimeOffset From, DateTimeOffset To);

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderNotificationService notifications) =>
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
                    return Results.BadRequest(new { error = "'from' must not be after 'to'." });
                }

                return await HandleAsync(new Request(fromValue, toValue), notifications);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, IOrderNotificationService notifications)
    {
        var report = await notifications.ReconcileAsync(request.From, request.To, CancellationToken.None);
        return Results.Ok(ReconciliationResponse.FromReport(report));
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result) && !string.IsNullOrWhiteSpace(value);
    }
}
