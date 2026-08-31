using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Anything still queued with the
/// provider for that number is called off, so nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IRepository<ContactNumber>, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeleteContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IRepository<ContactNumber> contactNumberRepository, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), contactNumberRepository, notificationService);
            })
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request,
        IRepository<ContactNumber> contactNumberRepository, IOrderNotificationService notificationService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var contactNumber = await contactNumberRepository.GetByIdAsync(request.ContactNumberId);
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        await notificationService.CancelPendingMessagesToNumberAsync(buyerId, contactNumber.PhoneNumber);
        await contactNumberRepository.DeleteAsync(contactNumber);

        return Results.NoContent();
    }
}
