using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }

    /// <summary>Caller-supplied idempotency key (from the Idempotency-Key header or the body).</summary>
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the notification the resend produced.</summary>
    public int NotificationId { get; set; }
}

/// <summary>
/// POST /api/notifications/{notificationId}/resend — operator re-sends a message that did not reach
/// the shopper. Repeating under the same idempotency key does not send a second message; a fresh key
/// sends anew. (Administrators.)
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, INotificationOperationsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? body, HttpContext http, INotificationOperationsService service) =>
            {
                // Header takes precedence; fall back to a body field.
                var key = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(key))
                    key = body?.IdempotencyKey;

                return await HandleAsync(new ResendNotificationRequest { NotificationId = notificationId, IdempotencyKey = key }, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, INotificationOperationsService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest("An idempotency key is required (Idempotency-Key header or idempotencyKey body field).");

        var notificationId = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
        return Results.Ok(new ResendNotificationResponse(request.CorrelationId()) { NotificationId = notificationId });
    }
}
