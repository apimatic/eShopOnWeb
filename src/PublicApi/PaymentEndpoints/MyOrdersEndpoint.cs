using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Lists the signed-in shopper's own orders with their payment state. Shopper-scoped.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService paymentService, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.GetBuyerId(user);
                var orders = await paymentService.GetOrdersForBuyerAsync(buyerId, ct);

                var response = new MyOrdersResponseDto
                {
                    Orders = orders.Select(PaymentViewMapping.ToOrderSummary).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<MyOrdersResponseDto>()
            .WithTags("PaymentEndpoints");
    }
}
