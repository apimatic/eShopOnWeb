using System.Collections.Generic;
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

public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationContentRequest>
{
    private readonly IOrderNotificationService _notifications;

    public RedactNotificationContentEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext httpContext) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest
                {
                    NotificationId = notificationId,
                    CancellationToken = httpContext.RequestAborted
                });
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationContentRequest request)
    {
        try
        {
            await _notifications.RedactContentAsync(request.NotificationId, request.CancellationToken);
            return Results.Ok(new { status = "Redacted" });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (OrderMessagingException)
        {
            return Results.Json(new { message = "The messaging provider is unavailable." }, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public class RedactNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; set; }
    internal CancellationToken CancellationToken { get; set; }
}
