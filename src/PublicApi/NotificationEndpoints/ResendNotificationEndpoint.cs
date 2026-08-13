using System;
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
/// caller-supplied idempotency key — repeating under the same key sends nothing new, while a genuine
/// second attempt under a fresh key is honoured. Returns the id of the message the resend produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request, HttpContext http, IOrderNotificationService notifications) =>
            {
                // The idempotency key may come in the body or an Idempotency-Key header.
                var key = request?.IdempotencyKey;
                if (string.IsNullOrWhiteSpace(key) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    key = header.ToString();
                }
                if (string.IsNullOrWhiteSpace(key))
                {
                    return Results.BadRequest("An idempotency key is required to resend.");
                }

                var result = await notifications.ResendAsync(notificationId, key!);
                return result.Outcome switch
                {
                    ResendOutcome.OriginalNotFound => Results.NotFound(),
                    ResendOutcome.DestinationRemoved => Results.Conflict("The number this message was sent to has been removed; nothing can be sent to it again."),
                    ResendOutcome.ContentDisposed => Results.Conflict("The message content was disposed of and cannot be re-sent."),
                    ResendOutcome.DuplicateIgnored => Results.Ok(new ResendNotificationResponse
                    {
                        NotificationId = result.NotificationId!.Value,
                        Duplicate = true
                    }),
                    _ => Results.Ok(new ResendNotificationResponse
                    {
                        NotificationId = result.NotificationId!.Value,
                        Duplicate = false
                    })
                };
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService notifications) =>
        Task.FromResult<IResult>(Results.Empty);
}

public class ResendNotificationRequest
{
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse
{
    /// <summary>Identifier of the message the resend produced (top-level field).</summary>
    public int NotificationId { get; set; }

    /// <summary>True when the idempotency key was seen before and nothing new was sent.</summary>
    public bool Duplicate { get; set; }
}
