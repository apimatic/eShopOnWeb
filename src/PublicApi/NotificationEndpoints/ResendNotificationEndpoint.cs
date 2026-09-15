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
/// Operator action: re-sends a message that did not reach the shopper. The request carries an
/// idempotency key — repeating under the same key does not send a second message, while a genuine
/// second attempt under a fresh key does. Returns the identifier of the message the re-send produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, INotificationService notifications) =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    return Results.BadRequest(new { message = "An idempotencyKey is required." });

                var result = await notifications.ResendAsync(notificationId, request.IdempotencyKey);
                if (result is null)
                    return Results.NotFound();

                return Results.Ok(new ResendNotificationResponse
                {
                    NotificationId = result.Id,
                    Status = result.Status
                });
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }
}

public class ResendNotificationRequest
{
    /// <summary>Caller-supplied key that makes the re-send idempotent.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse
{
    /// <summary>Identifier of the message the re-send produced (a new message, or the one an earlier identical request already produced).</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
}
