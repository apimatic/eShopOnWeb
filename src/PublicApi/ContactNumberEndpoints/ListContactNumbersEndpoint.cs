using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>GET /api/contact-numbers — the caller's registered numbers (only their own).</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ContactNumberEndpointServices>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ContactNumberEndpointServices services) => await HandleAsync(services))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ContactNumberEndpointServices services)
    {
        var buyerId = services.User.UserName();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var numbers = await services.ContactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));

        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(n => new ContactNumberDto
            {
                ContactNumberId = n.Id,
                PhoneNumber = n.PhoneNumber,
                CreatedDate = n.CreatedDate
            }).ToList()
        };
        return Results.Ok(response);
    }
}
