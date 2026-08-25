using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Lists the signed-in shopper's own orders, with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = user.Identity!.Name! }, orderPaymentService);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new MyOrdersResponse(request.CorrelationId());

        var orders = await orderPaymentService.GetOrdersForBuyerAsync(request.BuyerId);
        response.Orders = orders.Select(o => o.ToDto()).ToList();
        return Results.Ok(response);
    }
}
