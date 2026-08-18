using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the caller's registered numbers. Afterwards the number no longer appears among the
/// caller's numbers and nothing is sent to it again — any follow-up already queued to it is called off.
/// A shopper can only delete their own number.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IRepository<ContactNumber> _repository;
    private readonly IOrderNotificationService _notificationService;

    public DeleteContactNumberEndpoint(IRepository<ContactNumber> repository, IOrderNotificationService notificationService)
    {
        _repository = repository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user) => await HandleAsync(contactNumberId, user))
            .Produces<DeleteContactNumberResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var contactNumber = await _repository.GetByIdAsync(contactNumberId);
        // A number belongs to the shopper who registered it: another shopper's number is simply not found.
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
            return Results.NotFound();

        // Nothing may be sent to it again: call off any follow-up already queued to this number.
        await _notificationService.CancelScheduledForContactNumberAsync(buyerId, contactNumber.PhoneNumber);

        await _repository.DeleteAsync(contactNumber);

        return Results.Ok(new DeleteContactNumberResponse { ContactNumberId = contactNumberId });
    }
}
