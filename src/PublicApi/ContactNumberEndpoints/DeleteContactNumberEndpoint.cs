using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IRepository<ContactNumber> _contactNumbers;

    public DeleteContactNumberEndpoint(IRepository<ContactNumber> contactNumbers)
    {
        _contactNumbers = contactNumbers;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int contactNumberId, ClaimsPrincipal user) =>
                await HandleAsync(contactNumberId, user))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, ClaimsPrincipal user)
    {
        var buyerId = user.GetRequiredBuyerId();
        var existing = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId));
        if (existing == null)
        {
            return Results.NotFound();
        }

        await _contactNumbers.DeleteAsync(existing);
        return Results.NoContent();
    }
}
