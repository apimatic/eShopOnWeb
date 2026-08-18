using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: the provider's own record of messages sent from this application's configured
/// sending number over a date range, lined up against what eShop believes it sent — so a message
/// the provider knows about and eShop doesn't, or the reverse, is visible. Covers the whole range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderNotificationService service, CancellationToken ct) =>
            {
                if (!TryParseIso(from, out var fromDate) || !TryParseIso(to, out var toDate))
                {
                    return Results.BadRequest(new { message = "'from' and 'to' are required ISO-8601 date-times." });
                }
                if (fromDate > toDate)
                {
                    return Results.BadRequest(new { message = "'from' must not be after 'to'." });
                }

                try
                {
                    var report = await service.ReconcileAsync(fromDate, toDate, ct);
                    return Results.Ok(report);
                }
                catch (SmsGatewayException ex)
                {
                    return SmsErrorResults.ToResult(ex);
                }
            })
            .Produces<ReconciliationReport>()
            .WithTags("NotificationEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result);
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service) => Task.FromResult(Results.Ok());
}
