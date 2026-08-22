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
            (HttpContext httpContext, IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(new ListContactNumbersRequest { BuyerId = httpContext.GetBuyerId() }, contactNumberService);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, IContactNumberService contactNumberService)
    {
        var numbers = await contactNumberService.ListForBuyerAsync(request.BuyerId);
        var response = new ListContactNumbersResponse(request.CorrelationId());
        response.ContactNumbers.AddRange(numbers.Select(n => new ContactNumberDto
        {
            ContactNumberId = n.Id,
            PhoneNumber = n.CanonicalNumber
        }));
        return Results.Ok(response);
    }
}

public class ListContactNumbersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}
