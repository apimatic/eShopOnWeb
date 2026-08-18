using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — operator action. Lists the provider's own
/// record of messages sent from the configured sending number over the range and lines them up against what
/// eShop believes it sent. Counts only the configured sending number's messages.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest(new { error = "'to' must be on or after 'from'." });
        }

        var report = await service.ReconcileAsync(request.From, request.To);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            SendingNumber = report.SendingNumber,
            ProviderMessageCount = report.ProviderMessageCount,
            EShopMessageCount = report.EShopMessageCount,
            InSyncCount = report.InSyncCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                Sid = e.Sid,
                NotificationId = e.NotificationId,
                OrderId = e.OrderId,
                ProviderStatus = e.ProviderStatus,
                EShopStatus = e.EShopStatus,
                State = e.State.ToString()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
