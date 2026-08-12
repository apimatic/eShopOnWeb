using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action on a shopper's behalf: disposes of a message's content at the provider so its text is no
/// longer retrievable there, while the fact that the message was sent and what became of it survive.
/// Administrators only.
/// </summary>
public class RedactNotificationContentEndpoint : AuthenticatedEndpointBase,
    IEndpoint<IResult, RedactContentRequest, IOrderNotificationService>
{
    public RedactNotificationContentEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service) =>
                await HandleAsync(new RedactContentRequest(notificationId), service))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactContentRequest request, IOrderNotificationService service)
    {
        await service.RedactNotificationContentAsync(request.NotificationId, RequestAborted);
        return Results.NoContent();
    }
}
