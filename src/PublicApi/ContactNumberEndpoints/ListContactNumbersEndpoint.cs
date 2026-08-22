using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersEndpoint : IEndpoint<IResult, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IContactNumberService service, HttpContext http) =>
            {
                var buyerId = http.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var numbers = await service.ListForBuyerAsync(buyerId, http.RequestAborted);
                var response = new ListContactNumbersResponse
                {
                    ContactNumbers = numbers.Select(n => new ContactNumberDto
                    {
                        ContactNumberId = n.Id,
                        CanonicalNumber = n.CanonicalNumber,
                        CreatedAt = n.CreatedAt
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(IContactNumberService service)
    {
        return Task.FromResult<IResult>(Results.Ok());
    }
}
