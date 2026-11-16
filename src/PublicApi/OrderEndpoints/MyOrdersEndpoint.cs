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

/// <summary>
/// The caller's own orders with their current payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService paymentService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(paymentService, user);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderPaymentService paymentService, ClaimsPrincipal user)
    {
        var orders = await paymentService.GetMyOrdersAsync(user.GetUserName());

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(OrderResponseMapper.ToDto).ToList()
        };

        return Results.Ok(response);
    }
}
