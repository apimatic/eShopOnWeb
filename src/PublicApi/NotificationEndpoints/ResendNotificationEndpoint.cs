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
/// Operator action: re-sends a message that did not reach the shopper. The caller supplies an
/// idempotency key (the <c>Idempotency-Key</c> header, or an <c>idempotencyKey</c> query value).
/// Repeating under the same key returns the message already produced without sending another;
/// a fresh key produces a new message. The response carries the identifier of the message produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, IOrderNotificationService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService orderNotificationService, HttpContext http) =>
            {
                return await HandleAsync(notificationId, orderNotificationService, http);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService orderNotificationService, HttpContext http)
    {
        var idempotencyKey = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            idempotencyKey = http.Request.Query["idempotencyKey"].ToString();
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { error = "An idempotency key is required (Idempotency-Key header or idempotencyKey query value)." });
        }

        var result = await orderNotificationService.ResendAsync(notificationId, idempotencyKey.Trim(), http.RequestAborted);
        if (!result.Found || result.Notification is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new ResendNotificationResponse(Guid.NewGuid())
        {
            NotificationId = result.Notification.Id,
            Status = result.Notification.Status,
            Reused = result.Reused
        });
    }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message this re-send produced (or reused under the same key).</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>True when the key had already been used, so no new message was sent.</summary>
    public bool Reused { get; set; }
}
