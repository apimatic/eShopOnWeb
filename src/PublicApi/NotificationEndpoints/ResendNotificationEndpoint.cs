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
/// Operator action: re-sends a message that did not reach the shopper. The request carries a
/// caller-supplied idempotency key; repeating a request under the same key does not send a second
/// message, while a genuine second attempt under a fresh key does.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service) =>
            {
                return await HandleAsync(notificationId, request, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, IOrderNotificationService service)
    {
        var idempotencyKey = request?.IdempotencyKey ?? string.Empty;
        var result = await service.ResendAsync(notificationId, idempotencyKey);

        return result.Outcome switch
        {
            // Both a fresh send and a replayed duplicate return the produced notification's id.
            ResendOutcome.Sent or ResendOutcome.Duplicate => Results.Ok(new ResendNotificationResponse
            {
                NotificationId = result.Notification!.Id,
                Status = result.Notification.Status,
                Duplicate = result.Outcome == ResendOutcome.Duplicate
            }),
            ResendOutcome.NotFound => Results.NotFound(),
            _ => Results.Conflict(new { error = result.Error })
        };
    }
}

public class ResendNotificationRequest
{
    /// <summary>Caller-supplied key that makes a repeat of this request a no-op.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse
{
    public int NotificationId { get; init; }
    public string Status { get; init; } = string.Empty;

    /// <summary>True when this response replays a resend already produced under the same key.</summary>
    public bool Duplicate { get; init; }
}
