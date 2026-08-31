using System.Security.Claims;
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
/// Operator action: re-sends a message that did not reach the shopper. The request carries
/// a caller-supplied idempotency key; repeating under the same key does not send again.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, ClaimsPrincipal>
{
    private readonly IOrderNotificationService _notificationService;

    public ResendNotificationEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(notificationId, request, user);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, ClaimsPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        var result = await _notificationService.ResendAsync(notificationId, request.IdempotencyKey);

        return result.Outcome switch
        {
            ResendOutcome.NotFound => Results.NotFound(),
            ResendOutcome.DestinationRemoved => Results.Conflict(new { message = "The destination number is no longer on file; it must not be sent to again." }),
            ResendOutcome.ContentRedacted => Results.Conflict(new { message = "The message content has been disposed of and can no longer be sent." }),
            _ => Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.Notification!.Id,
                MessageSid = result.Notification.MessageSid,
                Status = result.Notification.ProviderStatus ?? "pending",
                IdempotentReplay = result.Outcome == ResendOutcome.IdempotentReplay
            })
        };
    }
}
