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

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest(from, to), service);
            })
            .Produces<ReconcileNotificationsResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, IOrderNotificationService service)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest(new { message = "'to' must be on or after 'from'." });
        }

        var report = await service.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconcileNotificationsResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Matched = report.Matched.Select(m => new ReconciledNotificationDto
            {
                NotificationId = m.Notification.Id,
                ProviderMessageSid = m.Notification.ProviderMessageSid,
                ApplicationStatus = m.Notification.Status,
                ProviderStatus = m.ProviderStatus,
                ProviderErrorCode = m.ProviderErrorCode
            }).ToList(),
            ProviderOnly = report.ProviderOnly.Select(p => new ProviderOnlyMessageDto
            {
                ProviderMessageSid = p.ProviderMessageSid,
                Status = p.Status,
                DateSent = p.DateSent,
                DateCreated = p.DateCreated
            }).ToList(),
            ApplicationOnly = report.ApplicationOnly.Select(NotificationDto.From).ToList()
        });
    }
}
