using System.Security.Claims;
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
/// Removes one of the caller's own registered numbers. Afterwards it no longer appears among the
/// caller's numbers and nothing is sent to it again. A shopper can only delete their own number.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> repository) =>
            {
                var ownerId = user.GetUserId();
                if (string.IsNullOrEmpty(ownerId))
                    return Results.Unauthorized();

                // Scope the lookup to the caller so one shopper can never delete another's number; a
                // number that isn't the caller's simply reads as "not found".
                var contactNumber = await repository.FirstOrDefaultAsync(
                    new ContactNumberByIdForOwnerSpecification(contactNumberId, ownerId));
                if (contactNumber is null)
                    return Results.NotFound();

                await repository.DeleteAsync(contactNumber);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }
}
