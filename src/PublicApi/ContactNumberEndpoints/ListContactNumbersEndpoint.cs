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

public class ListContactNumbersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest, IShopperContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IShopperContactNumberService contactNumbers) =>
            {
                var request = new ListContactNumbersRequest { BuyerId = httpContext.GetRequiredBuyerId() };
                return await HandleAsync(request, contactNumbers);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, IShopperContactNumberService contactNumbers)
    {
        var numbers = await contactNumbers.ListForBuyerAsync(request.BuyerId);
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(ContactNumberDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
