using System.Linq;
using System.Threading;
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
using Microsoft.eShopWeb.PublicApi.Middleware;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Re-sends a message that did not reach the shopper (operator action). The request
/// carries a caller-supplied idempotency key: a repeat under the same key returns the
/// message the first attempt produced; a fresh key is a genuine second attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IMessagingService _messagingService;

    public ResendNotificationEndpoint(
        IRepository<OrderNotification> notificationRepository,
        IRepository<ContactNumber> contactNumberRepository,
        IMessagingService messagingService)
    {
        _notificationRepository = notificationRepository;
        _contactNumberRepository = contactNumberRepository;
        _messagingService = messagingService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, CancellationToken ct) =>
            {
                return await HandleAsync(notificationId, request, ct);
            })
            .Produces<ResendNotificationResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "An idempotency key is required." });
        }

        var replay = await _notificationRepository.FirstOrDefaultAsync(
            new NotificationByIdempotencyKeySpecification(request.IdempotencyKey), ct);
        if (replay != null)
        {
            var replayResponse = new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = replay.Id,
                OriginalNotificationId = notificationId,
                Status = replay.Status,
                Replayed = true
            };
            return Results.Ok(replayResponse);
        }

        var original = await _notificationRepository.GetByIdAsync(notificationId, ct);
        if (original is null)
        {
            return Results.NotFound();
        }

        if (original.ContentRedacted || original.Body is null)
        {
            return Results.Conflict(new { message = "The content of this message has been disposed of; it cannot be re-sent." });
        }

        // A removed contact number must never be messaged again — the destination has
        // to still be one of the shopper's registered numbers.
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByBuyerSpecification(original.BuyerId), ct);
        var destination = numbers.FirstOrDefault(n => n.PhoneNumber == original.ToNumber);
        if (destination is null)
        {
            return Results.Conflict(new { message = "The destination number is no longer registered for this shopper." });
        }

        try
        {
            var message = await _messagingService.SendMessageAsync(original.ToNumber, original.Body, ct);
            var notification = new OrderNotification(original.OrderId, original.BuyerId, destination.Id,
                original.ToNumber, NotificationKind.Resend, original.Body, message.Sid,
                message.Status ?? "unknown", idempotencyKey: request.IdempotencyKey,
                errorCode: message.ErrorCode, errorMessage: message.ErrorMessage);
            await _notificationRepository.AddAsync(notification, ct);

            var response = new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = notification.Id,
                OriginalNotificationId = notificationId,
                Status = notification.Status
            };
            return Results.Created($"api/notifications/{notification.Id}", response);
        }
        catch (MessagingException ex)
        {
            return ProviderErrorResults.Map(ex);
        }
    }
}
