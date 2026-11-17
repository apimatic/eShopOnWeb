using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request,
             HttpContext httpContext,
             IOrderPaymentService paymentService,
             CancellationToken cancellationToken) =>
            {
                var order = await paymentService.CreateOrderAsync(
                    httpContext.User.GetBuyerId(),
                    request,
                    cancellationToken);

                return Results.Created($"api/orders/{order.OrderId}", order);
            })
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
