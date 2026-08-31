using System.Linq;
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
/// Lists the signed-in shopper's registered numbers.
/// </summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository) =>
            {
                return await HandleAsync(user, contactNumberRepository);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository)
    {
        var response = new ListContactNumbersResponse();
        var buyerId = user.Identity!.Name!;

        var contactNumbers = await contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        response.ContactNumbers.AddRange(contactNumbers.Select(c => new ContactNumberDto
        {
            ContactNumberId = c.Id,
            PhoneNumber = c.PhoneNumber,
            CreatedAt = c.CreatedAt
        }));

        return Results.Ok(response);
    }
}
