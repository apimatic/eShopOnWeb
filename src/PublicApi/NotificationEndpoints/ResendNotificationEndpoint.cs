using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller
/// supplies an idempotency key — repeating the request under the same key returns the
/// original resend without sending a second message.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IOrderNotificationService _notificationService;

    public ResendNotificationEndpoint(IRepository<OrderNotification> notificationRepository,
        IOrderNotificationService notificationService)
    {
        _notificationRepository = notificationRepository;
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
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .Produces<ResendNotificationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        var original = await _notificationRepository.GetByIdAsync(request.NotificationId);
        if (original is null)
        {
            return Results.NotFound();
        }

        ResendResult result;
        try
        {
            result = await _notificationService.ResendAsync(original, request.IdempotencyKey);
        }
        catch (NotificationContentRedactedException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
        catch (NotificationDestinationRemovedException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = result.Notification.Id,
            Status = result.Notification.ProviderStatus,
            ErrorCode = result.Notification.ProviderErrorCode,
            ErrorMessage = result.Notification.ProviderErrorMessage,
            AlreadyExisted = result.AlreadyExisted
        };

        return result.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created($"api/notifications/{result.Notification.Id}", response);
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public int NotificationId { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>The identifier of the message the resend produced.</summary>
    public int NotificationId { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool AlreadyExisted { get; set; }
}
