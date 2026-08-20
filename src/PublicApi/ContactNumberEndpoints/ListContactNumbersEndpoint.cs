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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IContactNumberService service, HttpContext httpContext) =>
            {
                return await HandleAsync(service, httpContext);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(IContactNumberService service)
        => HandleAsync(service, new DefaultHttpContext());

    private async Task<IResult> HandleAsync(IContactNumberService service, HttpContext httpContext)
    {
        var buyerId = httpContext.GetRequiredBuyerId();
        var numbers = await service.ListForBuyerAsync(buyerId);
        var response = new ListContactNumbersResponse();
        response.ContactNumbers.AddRange(numbers.Select(n => new ContactNumberDto
        {
            ContactNumberId = n.Id,
            PhoneNumber = n.PhoneNumber,
            NationalFormat = n.NationalFormat,
            CountryCode = n.CountryCode,
            LineType = n.LineType
        }));
        return Results.Ok(response);
    }
}
