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
    /// <summary>Caller-supplied idempotency key. A repeat under the same key does not send again.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Top-level identifier of the notification the resend produced.</summary>
    public int NotificationId { get; set; }

    /// <summary>True when a repeated idempotency key returned the earlier result without resending.</summary>
    public bool WasDuplicate { get; set; }
}

/// <summary>
/// Operator action: re-send a message that did not reach the shopper. Idempotent on a caller-supplied
/// key. Admin only.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest>
{
    private readonly IOrderMessagingService _orderMessagingService;

    public ResendNotificationEndpoint(IOrderMessagingService orderMessagingService)
    {
        _orderMessagingService = orderMessagingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request) => await HandleAsync(notificationId, request))
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            return Results.BadRequest(new { error = "An idempotencyKey is required." });

        using var cts = EndpointBudget.Start();
        var result = await _orderMessagingService.ResendAsync(notificationId, request.IdempotencyKey, cts.Token);
        if (!result.Found)
            return Results.NotFound();

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = result.NotificationId,
            WasDuplicate = result.WasDuplicate
        };
        return Results.Ok(response);
    }
}
