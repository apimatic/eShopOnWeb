using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: lists the provider's own record of messages for a date range (for the
/// configured sending number only) and lines them up against what eShop believes it sent, so a
/// message the provider knows about and eShop does not — or the reverse — is visible. Restricted to
/// the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), service);
            })
            .Produces<ReconciliationReport>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        if (!TryParseIso(request.From, out var from))
        {
            return Results.Problem(detail: "'from' must be an ISO-8601 date-time.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (!TryParseIso(request.To, out var to))
        {
            return Results.Problem(detail: "'to' must be an ISO-8601 date-time.", statusCode: StatusCodes.Status400BadRequest);
        }
        if (to < from)
        {
            return Results.Problem(detail: "'to' must not be earlier than 'from'.", statusCode: StatusCodes.Status400BadRequest);
        }

        var report = await service.ReconcileAsync(from, to);
        return Results.Ok(report);
    }

    private static bool TryParseIso(string? value, out DateTimeOffset parsed)
    {
        parsed = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed);
    }
}
