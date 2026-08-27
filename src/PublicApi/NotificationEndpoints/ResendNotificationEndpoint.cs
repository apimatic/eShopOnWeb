using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper.
/// The caller-supplied idempotency key makes repeats under the same key safe.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest>
{
    private readonly INotificationService _notificationService;

    public ResendNotificationEndpoint(INotificationService notificationService)
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
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        try
        {
            var resend = await _notificationService.ResendAsync(request.NotificationId, request.IdempotencyKey);
            return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = resend.Id,
                Status = resend.Status,
                ProviderMessageSid = resend.ProviderMessageSid
            });
        }
        catch (NotificationNotFoundException)
        {
            return Results.NotFound();
        }
        catch (NotificationContentRedactedException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) {}
    public ResendNotificationResponse() {}

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}
