using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Notifications;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberResponse : BaseResponse
{
    public int ContactNumberId { get; set; }
    public bool Deleted { get; set; }
}

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Any provider-queued messages still
/// addressed to the number are cancelled so nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, CancellationToken>
{
    private static readonly HashSet<string> TerminalStatuses = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "failed", "undelivered", "canceled"
    };

    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly TwilioMessagingService _messaging;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<DeleteContactNumberEndpoint> _logger;

    public DeleteContactNumberEndpoint(IRepository<ContactNumber> contactNumbers,
        IRepository<OrderNotification> notifications,
        TwilioMessagingService messaging,
        IHttpContextAccessor httpContextAccessor,
        ILogger<DeleteContactNumberEndpoint> logger)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
        _messaging = messaging;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, CancellationToken ct) =>
            {
                return await HandleAsync(contactNumberId, ct);
            })
            .Produces<DeleteContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, CancellationToken ct)
    {
        var ownerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var contactNumber = await _contactNumbers.GetByIdAsync(contactNumberId, ct);
        if (contactNumber is null || contactNumber.OwnerId != ownerId)
        {
            return Results.NotFound();
        }

        // Cancel anything still queued at the provider for this number.
        var pending = await _notifications.ListAsync(
            new PendingNotificationsByContactNumberSpecification(contactNumberId), ct);
        foreach (var notification in pending)
        {
            if (TerminalStatuses.Contains(notification.Status))
            {
                continue;
            }
            try
            {
                var outcome = await _messaging.CancelScheduledMessageAsync(notification.ProviderMessageSid!, ct);
                notification.UpdateProviderOutcome(outcome.Status, outcome.ErrorCode, outcome.ErrorMessage);
                await _notifications.UpdateAsync(notification, ct);
            }
            catch (MessagingException ex)
            {
                _logger.LogWarning(ex, "Queued notification {NotificationId} could not be cancelled while deleting contact number {ContactNumberId} (provider status {ProviderStatus}).",
                    notification.Id, contactNumberId, (int?)ex.ProviderStatusCode);
            }
        }

        await _contactNumbers.DeleteAsync(contactNumber, ct);

        return Results.Ok(new DeleteContactNumberResponse
        {
            ContactNumberId = contactNumberId,
            Deleted = true
        });
    }
}
