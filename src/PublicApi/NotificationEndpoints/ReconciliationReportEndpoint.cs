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

public class ReconciliationReportEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(from, to, notificationService);
            })
            .Produces<ReconciliationReportResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService notificationService)
        => Task.FromResult<IResult>(Results.BadRequest());

    private async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notificationService)
    {
        var report = await notificationService.ReconcileAsync(from, to);
        var response = new ReconciliationReportResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                NotificationId = m.NotificationId,
                ProviderMessageSid = m.ProviderMessageSid,
                EshopStatus = m.EshopStatus,
                ProviderStatus = m.ProviderStatus
            }).ToList(),
            ProviderOnly = report.ProviderOnly.Select(p => new ProviderOnlyMessageDto
            {
                ProviderMessageSid = p.Sid,
                ProviderStatus = p.Status,
                DateSent = p.DateSent
            }).ToList(),
            EshopOnly = report.EshopOnly.Select(NotificationDto.From).ToList()
        };

        return Results.Ok(response);
    }
}
