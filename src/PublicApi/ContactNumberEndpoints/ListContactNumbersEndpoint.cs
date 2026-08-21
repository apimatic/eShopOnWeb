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

public class ListContactNumbersEndpoint : IEndpoint<IResult, HttpContext, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IContactNumberService service) =>
            {
                return await HandleAsync(httpContext, service);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext httpContext, IContactNumberService service)
    {
        var numbers = await service.ListForBuyerAsync(httpContext.GetBuyerId());
        var response = new ListContactNumbersResponse();
        response.ContactNumbers.AddRange(numbers.Select(n => new ContactNumberDto
        {
            ContactNumberId = n.Id,
            PhoneNumber = n.PhoneNumber,
            NationalFormat = n.NationalFormat
        }));
        return Results.Ok(response);
    }
}
