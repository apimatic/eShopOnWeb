using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Returns the signed-in shopper's orders and their payment state, newest first.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                var buyerId = user.GetBuyerId();
                var orders = await orderPaymentService.GetOrdersForBuyerAsync(buyerId);

                var response = new MyOrdersResponse
                {
                    Orders = orders.Select(OrderDto.FromOrder).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Lists the caller's orders", "Returns the caller's orders with payment state."));
    }

    public Task<IResult> HandleAsync(IOrderPaymentService orderPaymentService) =>
        Task.FromResult(Results.Ok());
}
