using System;
using System.Linq;
using System.Security.Claims;
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

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Any provider-scheduled messages
/// still awaiting delivery to that number are cancelled so nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository,
                IRepository<OrderNotification> notificationRepository, ISmsNotificationClient smsClient, IAppLogger<DeleteContactNumberEndpoint> logger) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId) { BuyerId = user.Identity!.Name! },
                    contactNumberRepository, notificationRepository, smsClient, logger);
            })
            .Produces<DeleteContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(
        DeleteContactNumberRequest request,
        IRepository<ContactNumber> contactNumberRepository,
        IRepository<OrderNotification> notificationRepository,
        ISmsNotificationClient smsClient,
        IAppLogger<DeleteContactNumberEndpoint> logger)
    {
        var response = new DeleteContactNumberResponse(request.CorrelationId());

        var contactNumber = await contactNumberRepository.FirstOrDefaultAsync(new ContactNumberByIdSpecification(request.ContactNumberId));
        if (contactNumber is null || contactNumber.BuyerId != request.BuyerId)
        {
            return Results.NotFound();
        }

        await contactNumberRepository.DeleteAsync(contactNumber);

        // Cancel anything already queued with the provider for this number so it is never sent again.
        var pending = (await notificationRepository.ListAsync(new OrderNotificationsByBuyerSpecification(request.BuyerId)))
            .Where(n => n.ToNumber == contactNumber.PhoneNumber
                        && !string.IsNullOrEmpty(n.MessageSid)
                        && !NotificationStatus.IsTerminal(n.Status));
        foreach (var notification in pending)
        {
            try
            {
                var result = await smsClient.CancelScheduledMessageAsync(notification.MessageSid!);
                notification.UpdateStatus(result.Status, result.ErrorCode, result.ErrorMessage);
                await notificationRepository.UpdateAsync(notification);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Could not cancel pending notification {NotificationId} while deleting a contact number: {ErrorType}", notification.Id, ex.GetType().Name);
            }
        }

        response.Status = "deleted";
        return Results.Ok(response);
    }
}

public class DeleteContactNumberRequest : BaseRequest
{
    public DeleteContactNumberRequest(int contactNumberId)
    {
        ContactNumberId = contactNumberId;
    }

    public int ContactNumberId { get; }
    public string BuyerId { get; set; } = string.Empty;
}

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public DeleteContactNumberResponse() { }

    public string Status { get; set; } = string.Empty;
}
