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
/// Operator action: re-sends a message that did not reach the shopper. The request carries a
/// caller-supplied idempotency key — repeating a request under the same key does not send a second message,
/// while a genuine second attempt under a fresh key sends again. Administrators only.
/// </summary>
public class ResendNotificationEndpoint : AuthenticatedEndpointBase,
    IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public ResendNotificationEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendRequestBody body, IOrderNotificationService service) =>
                await HandleAsync(new ResendNotificationRequest(notificationId, body?.IdempotencyKey ?? string.Empty), service))
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotency key is required.");
        }

        var notification = await service.ResendAsync(request.NotificationId, request.IdempotencyKey, RequestAborted);

        var response = new ResendNotificationResponse
        {
            NotificationId = notification.Id,
            Status = notification.Status,
            MessageSid = notification.MessageSid
        };
        return Results.Ok(response);
    }
}
