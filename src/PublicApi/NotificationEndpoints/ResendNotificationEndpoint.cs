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
/// Re-sends a message that did not reach the shopper (operator). The caller-supplied
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
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request)
    {
        var result = await _notificationService.ResendAsync(notificationId, request.IdempotencyKey);

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = result.Notification.Id,
            ResendOfNotificationId = result.Notification.ResendOfNotificationId,
            Status = result.Notification.ProviderStatus,
            WasDuplicate = result.WasDuplicate
        };

        return result.WasDuplicate
            ? Results.Ok(response)
            : Results.Created($"api/notifications/{result.Notification.Id}", response);
    }
}

public class ResendNotificationRequest : BaseRequest
{
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

    public int NotificationId { get; set; }
    public int? ResendOfNotificationId { get; set; }
    public string? Status { get; set; }
    public bool WasDuplicate { get; set; }
}
