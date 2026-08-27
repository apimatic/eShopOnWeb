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

/// <summary>
/// Re-sends a message that did not reach the shopper (operator action). The
/// caller-supplied idempotency key suppresses duplicate sends: repeating the
/// request under the same key returns the original resend instead of sending
/// again.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest>
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
                request.NotificationId = notificationId;
                return await HandleAsync(request);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request)
    {
        var response = new ResendNotificationResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            response.Error = "An idempotency key is required.";
            return Results.BadRequest(response);
        }

        var result = await _notificationService.ResendAsync(request.NotificationId, request.IdempotencyKey);

        switch (result.Outcome)
        {
            case ResendNotificationOutcome.NotFound:
                return Results.NotFound();
            case ResendNotificationOutcome.ContentRedacted:
                response.Error = "The message content has been disposed of and can no longer be sent.";
                return Results.Conflict(response);
            case ResendNotificationOutcome.NoContactNumber:
                response.Error = "The shopper has no contact number on file.";
                return Results.Conflict(response);
            case ResendNotificationOutcome.AlreadyProcessed:
                response.NotificationId = result.Notification!.Id;
                response.Status = result.Notification.Status;
                response.Duplicate = true;
                return Results.Ok(response);
            default:
                response.NotificationId = result.Notification!.Id;
                response.Status = result.Notification.Status;
                return Results.Ok(response);
        }
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }

    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Duplicate { get; set; }
    public string? Error { get; set; }
}
