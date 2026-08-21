using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, HttpContext, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IOrderPaymentService service) =>
            {
                return await HandleAsync(http, service);
            })
            .Produces<ListOrdersApiResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IOrderPaymentService service)
    {
        var orders = await service.ListBuyerOrdersAsync(http.RequireBuyerId());
        return Results.Ok(new ListOrdersApiResponse
        {
            Orders = orders.Select(o => OrderDtoMapper.ToDto(o)).ToList()
        });
    }
}
