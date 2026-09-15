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

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }

    /// <summary>Caller-supplied idempotency key. The same key never sends a second message; a fresh key does.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Repeating the request under the
/// same idempotency key returns the same message without sending again.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpRequest httpRequest, IOrderNotificationService service) =>
            {
                request.NotificationId = notificationId;
                // Accept the idempotency key from the body, or from an Idempotency-Key header if the body omits it.
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && httpRequest.Headers.TryGetValue("Idempotency-Key", out var header))
                {
                    request.IdempotencyKey = header.ToString();
                }
                return await HandleAsync(request, service);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
    {
        var notificationId = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
        return Results.Ok(new ResendNotificationResponse(request.CorrelationId()) { NotificationId = notificationId });
    }
}
