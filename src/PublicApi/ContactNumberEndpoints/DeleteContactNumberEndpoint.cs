using System.Security.Claims;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the caller's contact numbers. Any provider-scheduled message to
/// it is called off so the number is never messaged again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user,
                IRepository<ContactNumber> contactNumberRepository, IOrderNotificationService notificationService,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest { ContactNumberId = contactNumberId },
                    user, contactNumberRepository, notificationService, cancellationToken);
            })
            .Produces<DeleteContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    private async Task<IResult> HandleAsync(DeleteContactNumberRequest request, ClaimsPrincipal user,
        IRepository<ContactNumber> contactNumberRepository, IOrderNotificationService notificationService, CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name ?? string.Empty;

        var contactNumber = await contactNumberRepository.GetByIdAsync(request.ContactNumberId, cancellationToken);
        if (contactNumber == null || contactNumber.BuyerId != buyerId)
        {
            throw new ContactNumberNotFoundException(request.ContactNumberId);
        }

        await notificationService.CancelPendingForContactNumberAsync(contactNumber.Id, cancellationToken);
        await contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);

        return Results.Ok(new DeleteContactNumberResponse(request.CorrelationId()) { ContactNumberId = request.ContactNumberId });
    }
}
