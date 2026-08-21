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
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListContactNumbersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IContactNumberService contactNumbers) =>
            {
                return await HandleAsync(contactNumbers);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(IContactNumberService contactNumbers)
    {
        var buyerId = HttpContextBuyer.GetBuyerId(_httpContextAccessor.HttpContext!);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var contacts = await contactNumbers.ListForBuyerAsync(buyerId);
        var response = new ListContactNumbersResponse();
        response.ContactNumbers.AddRange(contacts.Select(c => new ContactNumberDto
        {
            ContactNumberId = c.Id,
            PhoneNumber = c.CanonicalNumber
        }));

        return Results.Ok(response);
    }
}
