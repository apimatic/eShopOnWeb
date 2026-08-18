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
/// idempotency key via the <c>Idempotency-Key</c> header — repeating under the same key returns the
/// same result and sends nothing further; a fresh key is a genuine new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext httpContext, IOrderNotificationService service) =>
            {
                var idempotencyKey = httpContext.Request.Headers[IdempotencyHeader].ToString();
                var request = new ResendNotificationRequest
                {
                    NotificationId = notificationId,
                    IdempotencyKey = idempotencyKey
                };
                return await HandleAsync(request, service);
            })
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest(new { error = $"An '{IdempotencyHeader}' header is required." });

        var notification = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
        if (notification is null)
            return Results.NotFound();

        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            Status = notification.Status
        });
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ResendNotificationResponse()
    {
    }

    /// <summary>The identifier of the message the resend produced (top-level).</summary>
    public int NotificationId { get; set; }

    public string Status { get; set; } = string.Empty;
}
