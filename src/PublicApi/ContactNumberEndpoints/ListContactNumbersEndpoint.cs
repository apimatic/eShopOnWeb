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

public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IContactNumberService contactNumbers, HttpContext httpContext) =>
            {
                var request = new ListContactNumbersRequest { BuyerId = httpContext.GetBuyerId() };
                return await HandleAsync(request, contactNumbers);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, IContactNumberService contactNumbers)
    {
        var response = new ListContactNumbersResponse(request.CorrelationId());
        var items = await contactNumbers.ListForBuyerAsync(request.BuyerId);
        response.ContactNumbers.AddRange(items.Select(c => new ContactNumberDto
        {
            ContactNumberId = c.Id,
            PhoneNumber = c.PhoneNumber,
            CreatedAt = c.CreatedAt
        }));
        return Results.Ok(response);
    }
}
