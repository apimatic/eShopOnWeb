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

public class ListContactNumbersEndpoint : IEndpoint<IResult, EmptyRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(new EmptyRequest(), contactNumberService, httpContext);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, IContactNumberService contactNumberService)
        => HandleAsync(request, contactNumberService, null!);

    private async Task<IResult> HandleAsync(EmptyRequest request, IContactNumberService contactNumberService, HttpContext httpContext)
    {
        var contacts = await contactNumberService.ListForBuyerAsync(httpContext.GetBuyerId());
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = contacts.Select(c => new ContactNumberDto
            {
                ContactNumberId = c.Id,
                PhoneNumber = c.PhoneNumber,
                CreatedAt = c.CreatedAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}
