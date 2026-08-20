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

public class ListContactNumbersEndpoint : IEndpoint<IResult, IShopperContactService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IShopperContactService contactService, HttpContext httpContext) =>
            {
                return await HandleAsync(contactService, httpContext);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(IShopperContactService contactService)
        => HandleAsync(contactService, null!);

    private async Task<IResult> HandleAsync(IShopperContactService contactService, HttpContext httpContext)
    {
        var response = new ListContactNumbersResponse();
        var numbers = await contactService.ListForBuyerAsync(httpContext.GetRequiredBuyerId());
        response.ContactNumbers.AddRange(numbers.Select(number => new ContactNumberDto
        {
            ContactNumberId = number.Id,
            PhoneNumber = number.PhoneNumber,
            NationalFormat = number.NationalFormat,
            RegisteredAt = number.RegisteredAt
        }));
        return Results.Ok(response);
    }
}
