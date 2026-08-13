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
    /// <summary>Caller-supplied idempotency key: a repeat under the same key does not send again.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Top-level identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string? Status { get; set; }
    public string? ProviderMessageSid { get; set; }
}

/// <summary>
/// Operator action: re-send a message that did not reach the shopper. Idempotent on the
/// caller-supplied key.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service) =>
                await HandleAsync(notificationId, request, service))
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, IOrderNotificationService service)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotencyKey is required." });
        }

        var resend = await service.ResendAsync(notificationId, request.IdempotencyKey);

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = resend.Id,
            Status = resend.ProviderStatus,
            ProviderMessageSid = resend.ProviderMessageSid
        };
        return Results.Ok(response);
    }
}
