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

/// <summary>
/// Operator action (administrator only): re-sends a message that did not reach the shopper. The request
/// carries a caller-supplied idempotency key (the <c>Idempotency-Key</c> header, or an
/// <c>idempotencyKey</c> query value): repeating a request under the same key does not send a second
/// message and returns the notification the first request produced, while a fresh key is a genuine new
/// attempt. Returns the notificationId the resend produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, INotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId,
             [FromHeader(Name = "Idempotency-Key")] string? headerKey,
             [FromQuery] string? idempotencyKey,
             INotificationService service) =>
            {
                var key = !string.IsNullOrWhiteSpace(headerKey) ? headerKey : idempotencyKey;
                if (string.IsNullOrWhiteSpace(key))
                {
                    return Results.BadRequest(new { error = "An idempotency key is required (Idempotency-Key header or idempotencyKey query value)." });
                }

                var result = await service.ResendAsync(notificationId, key);
                return result.Outcome switch
                {
                    ResendOutcome.NotFound => Results.NotFound(),
                    ResendOutcome.Unresendable => Results.Conflict(new { error = result.Message }),
                    _ => Results.Ok(new ResendNotificationResponse
                    {
                        NotificationId = result.NotificationId,
                        Replayed = result.Outcome == ResendOutcome.Replayed
                    })
                };
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(INotificationService service) =>
        Task.FromResult<IResult>(Results.Empty);
}

public class ResendNotificationResponse
{
    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }

    /// <summary>True when this was an idempotent replay (no new message was sent).</summary>
    public bool Replayed { get; set; }
}
