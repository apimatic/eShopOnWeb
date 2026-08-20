using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class ListContactNumbersEndpoint : IEndpoint<IResult, HttpContext, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IContactNumberService service) =>
            {
                return await HandleAsync(http, service);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IContactNumberService service)
    {
        var buyerId = http.RequireBuyerId();
        var numbers = await service.ListForBuyerAsync(buyerId);
        return Results.Ok(new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(n => new ContactNumberDto
            {
                ContactNumberId = n.Id,
                PhoneNumber = n.CanonicalNumber
            }).ToList()
        });
    }
}
