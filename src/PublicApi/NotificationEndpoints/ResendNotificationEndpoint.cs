using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>
    /// Caller-supplied idempotency key: repeating a request under the same key
    /// must not send a second message.
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool AlreadyProcessed { get; set; }
}

/// <summary>
/// Re-sends a message that did not reach the shopper (operator).
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(notificationId, request, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notificationService)
    {
        throw new System.NotSupportedException();
    }

    private async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest("An idempotency key is required.");
        }

        var result = await notificationService.ResendAsync(notificationId, request.IdempotencyKey);

        return result.Result switch
        {
            ResendResult.NotFound => Results.NotFound(),
            ResendResult.DestinationRemoved => Results.Conflict(new
            {
                error = "The destination number is no longer on file; nothing may be sent to it again."
            }),
            ResendResult.ContentRedacted => Results.Conflict(new
            {
                error = "The message content has been disposed of and can no longer be sent."
            }),
            _ => Results.Ok(new ResendNotificationResponse
            {
                NotificationId = result.Notification!.Id,
                Status = result.Notification.ProviderStatus,
                ProviderMessageSid = result.Notification.ProviderMessageSid,
                ErrorCode = result.Notification.ErrorCode,
                ErrorMessage = result.Notification.ErrorMessage,
                AlreadyProcessed = result.Result == ResendResult.AlreadyProcessed
            })
        };
    }
}
