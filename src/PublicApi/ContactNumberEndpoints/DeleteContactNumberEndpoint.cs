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

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's registered numbers. Any provider-queued
/// (scheduled) messages to it are cancelled so nothing is ever sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IRepository<OrderNotification> _notificationRepository;
    private readonly IMessagingProvider _messagingProvider;
    private readonly IAppLogger<DeleteContactNumberEndpoint> _logger;

    public DeleteContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        IMessagingProvider messagingProvider,
        IAppLogger<DeleteContactNumberEndpoint> logger)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationRepository = notificationRepository;
        _messagingProvider = messagingProvider;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext httpContext) =>
            {
                return await HandleAsync(contactNumberId, httpContext);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, HttpContext httpContext)
    {
        var buyerId = httpContext.User.Identity!.Name!;
        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, httpContext.RequestAborted);

        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        // Cancel anything still queued with the provider for this destination.
        var scheduled = await _notificationRepository.ListAsync(
            new ScheduledNotificationsByContactNumberSpecification(contactNumberId), httpContext.RequestAborted);
        foreach (var notification in scheduled)
        {
            try
            {
                await _messagingProvider.CancelScheduledAsync(notification.ProviderMessageSid!, httpContext.RequestAborted);
                notification.UpdateStatus(OrderNotificationStatuses.Canceled);
                await _notificationRepository.UpdateAsync(notification, httpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to cancel scheduled notification {0} while removing a contact number: {1}",
                    notification.Id, ex.Message);
            }
        }

        await _contactNumberRepository.DeleteAsync(contactNumber, httpContext.RequestAborted);

        return Results.NoContent();
    }
}
