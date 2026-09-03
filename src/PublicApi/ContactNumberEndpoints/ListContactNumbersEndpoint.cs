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
            (IContactNumberService contactNumberService, HttpContext httpContext) =>
            {
                return await HandleAsync(new ListContactNumbersRequest(), httpContext, contactNumberService);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(ListContactNumbersRequest request, IContactNumberService contactNumberService)
        => HandleAsync(request, null!, contactNumberService);

    private async Task<IResult> HandleAsync(
        ListContactNumbersRequest request,
        HttpContext httpContext,
        IContactNumberService contactNumberService)
    {
        var response = new ListContactNumbersResponse(request.CorrelationId());
        var numbers = await contactNumberService.ListForBuyerAsync(httpContext.GetBuyerId(), httpContext.RequestAborted);
        response.ContactNumbers.AddRange(numbers.Select(n => new ContactNumberDto
        {
            ContactNumberId = n.Id,
            PhoneNumber = n.CanonicalNumber
        }));
        return Results.Ok(response);
    }
}
