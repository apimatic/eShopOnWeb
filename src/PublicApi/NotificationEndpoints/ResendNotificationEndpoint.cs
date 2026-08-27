using System;
using System.ComponentModel.DataAnnotations;
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
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message the resend produced (or of the original
    /// resend when the idempotency key was already used).</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool WasDuplicate { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied
/// idempotency key guarantees a repeated request does not send a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest>
{
    private readonly IOrderNotificationService _notificationService;

    public ResendNotificationEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request) =>
            {
                return await HandleAsync(notificationId, request);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "An idempotency key is required." });
        }

        var result = await _notificationService.ResendAsync(notificationId, request.IdempotencyKey);
        if (result.Notification is null)
        {
            return result.Error != null && result.Error.Contains("not found")
                ? Results.NotFound(new { error = result.Error })
                : Results.Conflict(new { error = result.Error });
        }

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = result.Notification.Id,
            Status = result.Notification.Status,
            WasDuplicate = result.WasDuplicate
        };
        return Results.Ok(response);
    }
}
