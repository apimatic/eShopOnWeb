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

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, IOperatorNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOperatorNotificationService operatorNotificationService) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest(from, to), operatorNotificationService);
            })
            .Produces<ReconcileNotificationsResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, IOperatorNotificationService operatorNotificationService)
    {
        var response = new ReconcileNotificationsResponse(request.CorrelationId());
        var report = await operatorNotificationService.ReconcileAsync(request.From, request.To);
        response.From = report.From;
        response.To = report.To;
        response.Matched = report.Matched.Select(ToDto).ToList();
        response.ProviderOnly = report.ProviderOnly.Select(ToDto).ToList();
        response.ApplicationOnly = report.ApplicationOnly.Select(ToDto).ToList();
        return Results.Ok(response);
    }

    private static ReconciledNotificationDto ToDto(ReconciledNotification item)
    {
        return new ReconciledNotificationDto
        {
            NotificationId = item.NotificationId,
            ProviderMessageSid = item.ProviderMessageSid,
            Status = item.Status,
            Body = item.Body,
            DateSent = item.DateSent,
            DateCreated = item.DateCreated
        };
    }
}
