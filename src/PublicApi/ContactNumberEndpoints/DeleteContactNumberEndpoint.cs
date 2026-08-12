using System.Security.Claims;
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
/// Removes one of the caller's contact numbers. Afterwards it no longer appears among the caller's
/// numbers and nothing can be sent to it again. A number that isn't the caller's own is reported as
/// not found, so one shopper can never delete another's.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, HttpContext, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext http, IRepository<ContactNumber> repository) =>
            {
                return await HandleAsync(contactNumberId, http, repository);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, HttpContext http, IRepository<ContactNumber> repository)
    {
        var buyerId = http.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var contactNumber = await repository.FirstOrDefaultAsync(new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId));
        if (contactNumber is null)
            return Results.NotFound();

        await repository.DeleteAsync(contactNumber);
        return Results.NoContent();
    }
}
