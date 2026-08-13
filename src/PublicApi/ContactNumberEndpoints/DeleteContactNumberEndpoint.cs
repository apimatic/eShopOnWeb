using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the caller's registered numbers. Afterwards it no longer appears among their numbers
/// and nothing may be sent to it again. A shopper can only remove their own number.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, System.Security.Claims.ClaimsPrincipal user, IRepository<ContactNumber> repository) =>
            {
                var owner = CallerIdentity.GetUserName(user);
                if (string.IsNullOrEmpty(owner))
                {
                    return Results.Unauthorized();
                }

                // Scope to owner + id so no shopper can remove (or probe for) another's number.
                var contactNumber = await repository.FirstOrDefaultAsync(
                    new ContactNumberByOwnerAndIdSpecification(owner, contactNumberId));
                if (contactNumber is null)
                {
                    return Results.NotFound();
                }

                await repository.DeleteAsync(contactNumber);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(IRepository<ContactNumber> repository) =>
        Task.FromResult<IResult>(Results.Empty);
}
