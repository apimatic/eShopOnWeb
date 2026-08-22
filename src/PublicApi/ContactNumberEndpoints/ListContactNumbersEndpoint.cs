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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IContactNumberService service) =>
            {
                return await HandleAsync(new EmptyRequest(), httpContext, service);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, IContactNumberService service)
        => HandleAsync(request, null!, service);

    private async Task<IResult> HandleAsync(EmptyRequest request, HttpContext httpContext, IContactNumberService service)
    {
        var buyerId = httpContext.BuyerId();
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await service.ListForBuyerAsync(buyerId, httpContext.RequestAborted);
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(n => new ContactNumberDto
            {
                ContactNumberId = n.Id,
                PhoneNumber = n.CanonicalNumber
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class EmptyRequest : BaseRequest
{
}
