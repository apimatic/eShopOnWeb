using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, INotificationOperatorService service) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest { From = from, To = to }, service);
            })
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, INotificationOperatorService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To, CancellationToken.None);
        return Results.Ok(new
        {
            from = report.From,
            to = report.To,
            providerMessageCount = report.ProviderMessageCount,
            localNotificationCount = report.LocalNotificationCount,
            matched = report.Matched.Select(m => new { providerSid = m.ProviderSid, notificationId = m.NotificationId }),
            providerOnly = report.ProviderOnly.Select(m => new
            {
                sid = m.Sid,
                status = m.Status,
                dateSent = m.DateSent,
                dateCreated = m.DateCreated
            }),
            localOnly = report.LocalOnly.Select(n => new
            {
                notificationId = n.Id,
                orderId = n.OrderId,
                providerSid = n.ProviderSid,
                status = n.ProviderStatus
            }),
            truncated = report.Truncated
        });
    }
}
