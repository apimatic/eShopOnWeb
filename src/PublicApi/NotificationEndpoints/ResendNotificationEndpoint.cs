using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The request
/// carries a caller-supplied idempotency key: a repeat under the same key returns the
/// original resend without sending a second message; a fresh key sends once more.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, int>
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IReadRepository<ContactNumber> _contactNumberRepository;
    private readonly IOrderNotificationService _notificationService;

    public ResendNotificationEndpoint(
        IRepository<OrderNotification> notificationRepository,
        IReadRepository<ContactNumber> contactNumberRepository,
        IOrderNotificationService notificationService)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ResendNotificationRequest request, int notificationId) =>
            {
                return await HandleAsync(request, notificationId);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, int notificationId)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId);
        if (original is null)
        {
            return Results.NotFound();
        }

        var alreadyProcessed = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(request.IdempotencyKey));
        if (alreadyProcessed != null)
        {
            return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = alreadyProcessed.Id,
                Status = alreadyProcessed.ProviderStatus,
                Deduplicated = true
            });
        }

        if (original.ContentRedacted || original.Body is null)
        {
            return Results.Conflict(new { message = "The content of this message has been disposed of and can no longer be sent." });
        }

        // A removed contact number must never be sent to again.
        var stillRegistered = await _contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByOwnerAndNumberSpecification(original.BuyerId, original.RecipientNumber));
        if (stillRegistered is null)
        {
            return Results.Conflict(new { message = "The recipient no longer has this number on file; it must not be sent to again." });
        }

        var resend = await _notificationService.SendResendAsync(original, request.IdempotencyKey);

        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = resend.Id,
            Status = resend.ProviderStatus,
            Deduplicated = false
        });
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) {}
    public ResendNotificationResponse() {}

    /// <summary>Identifier of the notification record the resend produced.</summary>
    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>True when the idempotency key was already used; no second message was sent.</summary>
    public bool Deduplicated { get; set; }
}
