using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Any provider-scheduled message to
/// it that has not yet gone out is called off first, so nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IOrderNotificationService _notificationService;

    public DeleteContactNumberEndpoint(
        IRepository<ContactNumber> contactNumberRepository,
        IOrderNotificationService notificationService)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, System.Security.Claims.ClaimsPrincipal user) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), user.GetBuyerId());
            })
            .Produces<DeleteContactNumberResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, string buyerId)
    {
        var contactNumber = await _contactNumberRepository.GetByIdAsync(request.ContactNumberId);
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        await _notificationService.CancelScheduledForContactNumberAsync(contactNumber.Id);
        await _contactNumberRepository.DeleteAsync(contactNumber);

        var response = new DeleteContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            Deleted = true
        };
        return Results.Ok(response);
    }
}
