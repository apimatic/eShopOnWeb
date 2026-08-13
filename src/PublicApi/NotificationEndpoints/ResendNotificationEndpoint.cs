using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// POST /api/notifications/{notificationId}/resend — operator action. Re-sends a message that did not
/// reach the shopper. The caller-supplied idempotency key makes a repeated request under the same key
/// a no-op (the same result is returned), while a fresh key is a genuine new attempt. Returns the
/// notificationId of the message the re-send produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext http, IOrderNotificationService service) =>
            {
                request.NotificationId = notificationId;
                // Accept the idempotency key from the body or, as a convenience, the Idempotency-Key header.
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    request.IdempotencyKey = header.ToString();
                }
                return await HandleAsync(request, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required for a re-send." });
        }

        var notification = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
        if (notification is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            Notification = NotificationMapping.ToDto(notification)
        });
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public string? IdempotencyKey { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int NotificationId { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Top-level identifier of the message the re-send produced.</summary>
    public int NotificationId { get; set; }
    public NotificationDto Notification { get; set; } = new();
}
