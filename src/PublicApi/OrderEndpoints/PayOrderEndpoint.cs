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
/// Authorizes (holds) the order total using either new card details or one of
/// the shopper's saved cards. Money is not captured until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId,
             PayOrderRequest request,
             HttpContext httpContext,
             IOrderPaymentService paymentService,
             CancellationToken cancellationToken) =>
            {
                var order = await paymentService.AuthorizeOrderAsync(
                    httpContext.User.GetBuyerId(),
                    orderId,
                    request,
                    cancellationToken);

                return Results.Ok(order);
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }
}
