using System;
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
/// Operator report: lines up the provider's own record of messages sent from this
/// application's configured sending number over a date range against what eShop believes it
/// sent, so a message the provider knows about and eShop doesn't — or the reverse — is visible.
/// Restricted to administrators. <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notificationService) =>
                await HandleAsync(from, to, notificationService))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public static async Task<IResult> HandleAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        IOrderNotificationService notificationService)
    {
        if (to < from)
        {
            return Results.Problem("'to' must not be earlier than 'from'.", statusCode: StatusCodes.Status400BadRequest);
        }

        var report = await notificationService.ReconcileAsync(from, to);

        var response = new ReconciliationResponse(
            report.From,
            report.To,
            report.Matched.Count,
            report.ProviderOnly.Count,
            report.EShopOnly.Count,
            report.Matched,
            report.ProviderOnly,
            report.EShopOnly);

        return Results.Ok(response);
    }
}

public record ReconciliationResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    int MatchedCount,
    int ProviderOnlyCount,
    int EShopOnlyCount,
    System.Collections.Generic.IReadOnlyList<ReconciliationMatch> Matched,
    System.Collections.Generic.IReadOnlyList<ReconciliationProviderRecord> ProviderOnly,
    System.Collections.Generic.IReadOnlyList<ReconciliationEShopRecord> EShopOnly);
