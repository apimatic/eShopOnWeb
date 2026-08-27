using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied
/// idempotency key guarantees that repeating the same request does not send a second
/// message; a fresh key is a genuine new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IRepository<OrderNotification>>
{
    private readonly INotificationGateway _gateway;
    private readonly IAppLogger<ResendNotificationEndpoint> _logger;

    public ResendNotificationEndpoint(INotificationGateway gateway,
        IAppLogger<ResendNotificationEndpoint> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequestBody body, IRepository<OrderNotification> notificationRepository) =>
            {
                return await HandleAsync(new ResendNotificationRequest(notificationId, body), notificationRepository);
            })
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IRepository<OrderNotification> notificationRepository)
    {
        if (string.IsNullOrWhiteSpace(request.Body.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotencyKey is required." });
        }

        var existing = await notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(request.Body.IdempotencyKey));
        if (existing is not null)
        {
            return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = existing.Id,
                Status = existing.Status,
                Duplicate = true
            });
        }

        var original = await notificationRepository.GetByIdAsync(request.NotificationId);
        if (original is null)
        {
            return Results.NotFound();
        }

        if (original.ContentRedacted || original.Body is null)
        {
            return Results.Conflict(new { message = "The content of this message has been disposed of; it cannot be re-sent." });
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, NotificationKind.Resend,
            original.ToNumber, original.Body, idempotencyKey: request.Body.IdempotencyKey,
            originalNotificationId: original.Id);

        try
        {
            var message = await _gateway.SendMessageAsync(original.ToNumber, original.Body);
            resend.MarkAccepted(message.Sid, message.Status ?? "accepted");
            resend.UpdateFromProvider(message.Status ?? "accepted", message.ErrorCode, message.ErrorMessage);
        }
        catch (NotificationProviderException ex)
        {
            resend.MarkFailed(NotificationStatuses.Failed, ex.ProviderErrorCode, null);
            await notificationRepository.AddAsync(resend);
            return Results.Json(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = resend.Id,
                Status = resend.Status
            }, statusCode: StatusCodes.Status502BadGateway);
        }

        await notificationRepository.AddAsync(resend);

        return Results.Created($"api/notifications/{resend.Id}", new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = resend.Id,
            Status = resend.Status
        });
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public ResendNotificationRequest(int notificationId, ResendNotificationRequestBody body)
    {
        NotificationId = notificationId;
        Body = body;
    }

    public int NotificationId { get; }
    public ResendNotificationRequestBody Body { get; }
}

public class ResendNotificationRequestBody
{
    [Required]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) {}
    public ResendNotificationResponse() {}

    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Duplicate { get; set; }
}
