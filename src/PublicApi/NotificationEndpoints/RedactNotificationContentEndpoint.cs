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
/// Operator action: disposes of a message's content on request. The text is redacted at
/// the provider and cleared locally, while the fact it was sent and its outcome survive.
/// </summary>
public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationContentRequest, INotificationOperationsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, INotificationOperationsService service) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest { NotificationId = notificationId }, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationContentRequest request, INotificationOperationsService service)
    {
        var redacted = await service.RedactContentAsync(request.NotificationId);
        return redacted ? Results.NoContent() : Results.NotFound();
    }
}

public class RedactNotificationContentRequest
{
    public int NotificationId { get; set; }
}
