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
/// Operator action: lists the provider's own record of messages sent from this application's
/// configured sending number over a date range, lined up against what eShop believes it sent, so a
/// message one side knows about and the other doesn't is visible. Restricted to administrators.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, string, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOrderNotificationService service, CancellationToken ct) =>
            {
                if (!TryParseIso(from, out var fromValue))
                {
                    return Results.BadRequest(new { error = "'from' must be an ISO-8601 date-time." });
                }

                if (!TryParseIso(to, out var toValue))
                {
                    return Results.BadRequest(new { error = "'to' must be an ISO-8601 date-time." });
                }

                if (fromValue > toValue)
                {
                    return Results.BadRequest(new { error = "'from' must not be after 'to'." });
                }

                var report = await service.ReconcileAsync(fromValue, toValue, ct);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    private static bool TryParseIso(string value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal,
            out result);

    public Task<IResult> HandleAsync(string request, IOrderNotificationService service) =>
        Task.FromResult(Results.Ok());
}
