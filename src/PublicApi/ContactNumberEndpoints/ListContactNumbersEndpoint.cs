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
            (HttpContext httpContext, IContactNumberService service) =>
            {
                var unauthorized = httpContext.UnauthorizedIfAnonymous();
                if (unauthorized is not null) return unauthorized;
                return await HandleAsync(service, httpContext.GetBuyerId()!);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(IContactNumberService service) => HandleAsync(service, string.Empty);

    private async Task<IResult> HandleAsync(IContactNumberService service, string buyerId)
    {
        var contacts = await service.ListForBuyerAsync(buyerId, default);
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = contacts.Select(c => new ContactNumberDto
            {
                ContactNumberId = c.Id,
                Number = c.Number
            }).ToList()
        };
        return Results.Ok(response);
    }
}
