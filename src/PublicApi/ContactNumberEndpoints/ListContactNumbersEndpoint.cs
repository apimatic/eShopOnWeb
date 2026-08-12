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

/// <summary>Lists the caller's own registered contact numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, string, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<ContactNumber> repository) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(buyerId, repository);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IRepository<ContactNumber> repository)
    {
        var numbers = await repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(cn => new ContactNumberDto
            {
                ContactNumberId = cn.Id,
                PhoneNumber = cn.PhoneNumber,
                RegisteredAt = cn.RegisteredAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}
