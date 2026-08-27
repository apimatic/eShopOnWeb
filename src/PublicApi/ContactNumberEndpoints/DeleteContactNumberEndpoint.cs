using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Any not-yet-sent messages to
/// the number are cancelled with the provider so nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IOrderNotificationService _notificationService;

    public DeleteContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository,
        IOrderNotificationService notificationService)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal claimsPrincipal) =>
            {
                return await HandleAsync(contactNumberId, claimsPrincipal);
            })
            .Produces<DeleteContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, ClaimsPrincipal claimsPrincipal)
    {
        var buyerId = claimsPrincipal.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId);
        if (contactNumber == null || contactNumber.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        await _notificationService.SuppressPendingMessagesToAsync(contactNumber);
        await _contactNumberRepository.DeleteAsync(contactNumber);

        return Results.Ok(new DeleteContactNumberResponse { ContactNumberId = contactNumberId });
    }
}

public class DeleteContactNumberResponse : BaseResponse
{
    public int ContactNumberId { get; set; }
}
