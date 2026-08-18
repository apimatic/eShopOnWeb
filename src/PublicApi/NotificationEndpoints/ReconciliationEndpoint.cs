using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// Operator action: lists the provider's own record of messages for the configured sender over a date
/// range and lines them up against what eShop believes it sent, surfacing either-side discrepancies.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
                await HandleAsync(new ReconciliationRequest(from, to), service))
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        if (request.To < request.From)
            return Results.BadRequest(new { error = "'to' must not be earlier than 'from'." });

        var report = await service.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            ProviderCount = report.ProviderCount,
            EShopCount = report.EShopCount,
            MatchedCount = report.Matched.Count,
            Matched = report.Matched.Select(Map).ToList(),
            ProviderOnly = report.ProviderOnly.Select(Map).ToList(),
            EShopOnly = report.EShopOnly.Select(Map).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationEntryDto Map(ReconciliationEntry e) => new()
    {
        Sid = e.Sid,
        NotificationId = e.NotificationId,
        ProviderStatus = e.ProviderStatus,
        EShopStatus = e.EShopStatus
    };
}
