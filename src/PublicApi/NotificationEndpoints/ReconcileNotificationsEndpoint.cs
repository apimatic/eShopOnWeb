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

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
            {
                return await HandleAsync(from, to, service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service)
        => throw new InvalidOperationException("Use the routed handler.");

    private async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service)
    {
        var report = await service.ReconcileAsync(from, to);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            LocalOnlyCount = report.LocalOnlyCount,
            Items = report.Items.Select(i => new ReconciliationItemDto
            {
                NotificationId = i.LocalNotificationId,
                ProviderMessageSid = i.ProviderMessageSid,
                Match = i.Match,
                LocalStatus = i.LocalStatus,
                ProviderStatus = i.ProviderStatus,
                DateCreated = i.DateCreated,
                DateSent = i.DateSent
            }).ToList()
        });
    }
}
