using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's content on a shopper's request. Afterwards the text is no
/// longer retrievable from the provider, while the fact that a message was sent and its outcome survive.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext http, IOrderNotificationService service) =>
                await HandleAsync(notificationId, service, http.RequestAborted))
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService service, CancellationToken ct)
    {
        try
        {
            var disposed = await service.DisposeContentAsync(notificationId, ct);
            return disposed ? Results.NoContent() : Results.NotFound();
        }
        catch (SmsGatewayException ex)
        {
            // The content could not be disposed at the provider — report it rather than pretend it is gone.
            return Results.Problem(
                title: "The message content could not be disposed at the provider.",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
