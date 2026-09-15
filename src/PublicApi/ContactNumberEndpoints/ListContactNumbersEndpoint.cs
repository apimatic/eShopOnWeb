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

public class ListContactNumbersEndpoint : IEndpoint<IResult, HttpContext>
{
    private readonly IContactNumberService _contactNumberService;

    public ListContactNumbersEndpoint(IContactNumberService contactNumberService)
    {
        _contactNumberService = contactNumberService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext) =>
            {
                return await HandleAsync(httpContext);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        var buyerId = httpContext.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await _contactNumberService.ListForBuyerAsync(buyerId);
        var response = new ListContactNumbersResponse();
        response.ContactNumbers.AddRange(numbers.Select(c => new ContactNumberDto
        {
            ContactNumberId = c.Id,
            PhoneNumber = c.PhoneNumber,
            NationalFormat = c.NationalFormat,
            CountryCode = c.CountryCode,
            CreatedAt = c.CreatedAt
        }));

        return Results.Ok(response);
    }
}
