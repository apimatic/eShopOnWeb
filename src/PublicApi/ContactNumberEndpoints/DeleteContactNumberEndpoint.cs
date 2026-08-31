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
/// Removes one of the signed-in shopper's contact numbers. Nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, ClaimsPrincipal, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), user, contactNumberRepository);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var contactNumber = await contactNumberRepository.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpecification(request.ContactNumberId, buyerId));
        if (contactNumber == null)
        {
            return Results.NotFound();
        }

        await contactNumberRepository.DeleteAsync(contactNumber);
        return Results.NoContent();
    }
}
