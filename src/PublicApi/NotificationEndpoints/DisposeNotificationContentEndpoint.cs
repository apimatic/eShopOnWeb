using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DisposeNotificationContentEndpoint : IEndpoint<IResult, DisposeNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new DisposeNotificationContentRequest(notificationId), service, cancellationToken);
            })
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(DisposeNotificationContentRequest request, IOrderNotificationService service)
        => HandleAsync(request, service, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        DisposeNotificationContentRequest request,
        IOrderNotificationService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var notification = await service.DisposeContentAsync(request.NotificationId, cancellationToken);
            return Results.Ok(new
            {
                notificationId = notification.Id,
                contentRedacted = notification.ContentRedacted,
                status = notification.Status,
                providerSid = notification.ProviderSid
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }
}

public class DisposeNotificationContentRequest : BaseRequest
{
    public DisposeNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; }
}
