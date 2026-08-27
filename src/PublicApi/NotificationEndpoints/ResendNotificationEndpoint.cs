using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    /// <summary>Caller-supplied idempotency key. Repeating a request under the same key does not send again.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    /// <summary>Identifier of the notification the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>True when the idempotency key was already used and no second message was sent.</summary>
    public bool Duplicate { get; set; }
}

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. Idempotent on the
/// caller-supplied key: a repeated request under the same key returns the notification the
/// first attempt produced without sending again.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, HttpContext>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IMessagingProvider _messagingProvider;

    public ResendNotificationEndpoint(IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IMessagingProvider messagingProvider)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _messagingProvider = messagingProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(notificationId, request, httpContext);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, httpContext.RequestAborted);
        if (original is null)
        {
            return Results.NotFound();
        }

        var existing = await _notificationRepository.FirstOrDefaultAsync(
            new OrderNotificationByResendKeySpecification(request.IdempotencyKey), httpContext.RequestAborted);
        if (existing is not null)
        {
            return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = existing.Id,
                Status = existing.Status,
                Duplicate = true
            });
        }

        if (original.ContentRedacted || original.Body is null)
        {
            return Results.Conflict(new { message = "The content of this message has been disposed of and it can no longer be re-sent." });
        }

        var contactNumber = await _contactNumberRepository.GetByIdAsync(original.ContactNumberId, httpContext.RequestAborted);
        if (contactNumber is null)
        {
            return Results.Conflict(new { message = "The destination number is no longer registered; nothing may be sent to it." });
        }

        ProviderMessageResult result;
        try
        {
            result = await _messagingProvider.SendAsync(contactNumber.PhoneNumber, original.Body, httpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            return Results.Json(new { message = $"The provider could not send the message: {ex.Message}" },
                statusCode: StatusCodes.Status502BadGateway);
        }

        var resend = new OrderNotification(original.OrderId, original.BuyerId, original.ContactNumberId,
            original.Type, original.Body, result.ProviderMessageSid, result.Status,
            resendIdempotencyKey: request.IdempotencyKey);
        resend = await _notificationRepository.AddAsync(resend, httpContext.RequestAborted);

        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = resend.Id,
            Status = resend.Status,
            Duplicate = false
        });
    }
}
