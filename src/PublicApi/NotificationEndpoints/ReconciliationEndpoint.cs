using System;
using System.Linq;
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
/// Operator report: lists the provider's own record of messages for a date range and lines them up against
/// what eShop believes it sent, so a message the provider knows about and eShop doesn't — or the reverse —
/// is visible. Only messages sent from this application's configured sending number are counted.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, INotificationOperationsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, INotificationOperationsService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, INotificationOperationsService service)
    {
        if (request.From > request.To)
        {
            return Results.BadRequest("'from' must be on or before 'to'.");
        }

        var report = await service.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            ProviderCount = report.ProviderCount,
            EShopCount = report.EShopCount,
            MatchedCount = report.MatchedCount,
            Matched = report.Matched.Select(Map).ToList(),
            ProviderOnly = report.ProviderOnly.Select(Map).ToList(),
            EShopOnly = report.EShopOnly.Select(Map).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationEntryDto Map(ReconciliationEntry entry) => new()
    {
        Sid = entry.Sid,
        InProvider = entry.InProvider,
        InEShop = entry.InEShop,
        ProviderStatus = entry.ProviderStatus,
        EShopStatus = entry.EShopStatus,
        NotificationId = entry.NotificationId,
        OrderId = entry.OrderId
    };
}
