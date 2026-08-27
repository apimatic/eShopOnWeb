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
/// Operator action: re-sends a message that did not reach the shopper. The
/// caller-supplied idempotency key makes repeats safe.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest>
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
            (int notificationId, ResendNotificationRequest request) =>
            {
                return await HandleAsync(notificationId, request);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "IdempotencyKey is required." });
        }

        var result = await _notificationService.ResendAsync(notificationId, request.IdempotencyKey);

        return result.Status switch
        {
            ResendStatus.NotFound => Results.NotFound(),
            ResendStatus.ContentRedacted => Results.Conflict(new { message = "The message content has been disposed of and cannot be re-sent." }),
            ResendStatus.ContactNumberRemoved => Results.Conflict(new { message = "The shopper's contact number was removed; it must not be sent to again." }),
            ResendStatus.SendFailed => Results.Json(new
            {
                notificationId = result.Notification!.Id,
                status = result.Notification.ProviderStatus,
                message = "The provider rejected the re-send; the attempt was recorded."
            }, statusCode: StatusCodes.Status502BadGateway),
            _ => Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.Notification!.Id,
                Status = result.Notification.ProviderStatus,
                ProviderMessageSid = result.Notification.ProviderMessageSid,
                Duplicate = result.Status == ResendStatus.DuplicateRequest
            })
        };
    }
}
