using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Shared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the caller's registered numbers. Afterwards it no longer appears among the
/// caller's numbers and nothing is ever sent to it again. A number belonging to another shopper is
/// treated as not found.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> repository) =>
            {
                return await HandleAsync(contactNumberId, user, repository);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    private static async Task<IResult> HandleAsync(int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> repository)
    {
        var ownerId = user.UserId();
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var contactNumber = await repository.GetByIdAsync(contactNumberId);
        if (contactNumber is null || contactNumber.OwnerId != ownerId)
        {
            // Never reveal, or let a caller act on, another shopper's number.
            return Results.NotFound();
        }

        await repository.DeleteAsync(contactNumber);
        return Results.NoContent();
    }
}
