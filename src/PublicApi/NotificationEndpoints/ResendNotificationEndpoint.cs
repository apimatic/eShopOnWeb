using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest
{
    /// <summary>Caller-supplied idempotency key. The same key never sends a second message.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse
{
    /// <summary>The identifier of the message the re-send produced (new, or the earlier one under the same key).</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

/// <summary>
/// POST /api/notifications/{notificationId}/resend — operator action. Re-sends a message that
/// did not reach the shopper. Repeating under the same idempotency key does not send again; a
/// fresh key is a legitimate new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int notificationId,
                [FromBody] ResendNotificationRequest request,
                IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                if (request is null || string.IsNullOrWhiteSpace(request.IdempotencyKey))
                    return Results.BadRequest(new { message = "An idempotency key is required." });

                var result = await notificationService.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
                if (result is null)
                    return Results.NotFound();

                return Results.Ok(new ResendNotificationResponse
                {
                    NotificationId = result.Id,
                    Status = result.Status,
                    ProviderMessageSid = result.ProviderMessageSid
                });
            })
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }
}
