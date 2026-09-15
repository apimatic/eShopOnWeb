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
/// Removes one of the signed-in shopper's registered numbers. Afterwards it no longer appears among
/// the caller's numbers, and any follow-up still queued for it with the provider is called off so
/// nothing is sent to it again. A shopper can only remove their own numbers.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> repository, INotificationService notifications) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var contactNumber = await repository.GetByIdAsync(contactNumberId);
                // Not found, or owned by someone else — reveal nothing either way.
                if (contactNumber is null || contactNumber.BuyerId != buyerId)
                    return Results.NotFound();

                await notifications.CancelPendingForNumberAsync(buyerId, contactNumber.PhoneNumber);
                await repository.DeleteAsync(contactNumber);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }
}
