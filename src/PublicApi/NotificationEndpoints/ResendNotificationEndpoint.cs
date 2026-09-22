using System.Threading;
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
/// Operator action: re-send a message that did not reach the shopper. Idempotent on a caller-supplied key —
/// repeating under the same key does not send again; a fresh key is a genuine second attempt.
/// </summary>
public class ResendNotificationEndpoint
    : IEndpoint<IResult, ResendNotificationRequest, IOperatorOrderService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOperatorOrderService service,
                CancellationToken ct) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, service, ct);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOperatorOrderService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "An idempotencyKey is required." });
        }

        // A failed send is recorded (never thrown) — the resend action succeeds and the produced
        // notification carries the delivery outcome the operator can inspect.
        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey!, ct);
        if (result.NotFound)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = result.NotificationId,
            Duplicate = result.WasDuplicate
        });
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    /// <summary>The notification id of the message the resend produced (or the existing one, on a repeat).</summary>
    public int NotificationId { get; set; }

    /// <summary>True when this request repeated a prior key and no new message was sent.</summary>
    public bool Duplicate { get; set; }
}
