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

/// <summary>
/// Operator re-sends a message that did not reach the shopper. Idempotent on the caller-supplied
/// <c>Idempotency-Key</c> header: a repeat under the same key sends nothing and returns the notification the
/// first call produced; a fresh key is a legitimate new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
             IOrderNotificationService service, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(idempotencyKey))
                    return Results.BadRequest(new { message = "An Idempotency-Key header is required." });

                var notification = await service.ResendAsync(notificationId, idempotencyKey, ct);
                if (notification is null)
                    return Results.NotFound();

                return Results.Ok(new ResendNotificationResponse { NotificationId = notification.Id });
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service) => Task.FromResult(Results.Ok());
}

public class ResendNotificationResponse
{
    /// <summary>The identifier of the message the resend produced (the same one on an idempotent repeat).</summary>
    public int NotificationId { get; set; }
}
