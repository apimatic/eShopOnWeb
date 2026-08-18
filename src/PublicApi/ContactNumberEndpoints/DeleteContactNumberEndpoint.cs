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
using Microsoft.eShopWeb.PublicApi.NotificationsFeature;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's numbers. Scoped to the caller's own numbers, so one
/// shopper can never delete another's. Afterwards the number no longer appears among the
/// caller's numbers and nothing is sent to it again (sending resolves current numbers only).
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> repository) =>
                await HandleAsync(contactNumberId, user, repository))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public static async Task<IResult> HandleAsync(int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> repository)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        // Only the caller's own number can be found here, so another shopper's number is a 404,
        // not a deletion.
        var contactNumber = await repository.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(contactNumberId, buyerId));
        if (contactNumber is null)
        {
            return Results.NotFound();
        }

        await repository.DeleteAsync(contactNumber);
        return Results.NoContent();
    }
}
