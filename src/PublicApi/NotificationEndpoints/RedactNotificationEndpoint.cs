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

public class RedactNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
}

public class RedactNotificationEndpoint : IEndpoint<IResult, RedactNotificationRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationOperatorService service) =>
            {
                return await HandleAsync(new RedactNotificationRequest { NotificationId = notificationId }, service);
            })
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationRequest request, INotificationOperatorService service)
    {
        var notification = await service.RedactContentAsync(request.NotificationId, CancellationToken.None);
        return Results.Ok(new
        {
            notificationId = notification.Id,
            bodyRedacted = notification.BodyRedacted,
            status = notification.ProviderStatus
        });
    }
}
