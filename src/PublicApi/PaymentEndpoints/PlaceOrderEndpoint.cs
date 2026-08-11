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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper.
/// The order starts awaiting payment. Returns the new order id as a top-level field.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                PlaceOrderRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService orderPaymentService,
                CancellationToken cancellationToken) =>
            await PaymentProblem.ExecuteAsync(async () =>
            {
                var buyerId = user.GetBuyerId();
                var lines = (request.Items ?? new())
                    .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
                    .ToList();

                var orderId = await orderPaymentService.PlaceOrderAsync(buyerId, lines, cancellationToken);

                return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse { OrderId = orderId });
            }))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
