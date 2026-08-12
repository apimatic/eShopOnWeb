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

public class ReconciliationRequest : BaseRequest
{
    public string? From { get; set; }
    public string? To { get; set; }
}

/// <summary>
/// Operator action: lists the provider's own record of messages from the configured sending number for
/// an ISO-8601 date-time range and lines them up against what eShop believes it sent, so a message one
/// side knows about and the other does not is visible. Restricted to administrators.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, INotificationAdminService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, INotificationAdminService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, INotificationAdminService service)
    {
        if (!TryParseIso(request.From, out var from))
        {
            return Results.BadRequest(new { error = "'from' must be an ISO-8601 date-time." });
        }
        if (!TryParseIso(request.To, out var to))
        {
            return Results.BadRequest(new { error = "'to' must be an ISO-8601 date-time." });
        }
        if (from > to)
        {
            return Results.BadRequest(new { error = "'from' must not be after 'to'." });
        }

        var report = await service.ReconcileAsync(from, to);
        return Results.Ok(report);
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
