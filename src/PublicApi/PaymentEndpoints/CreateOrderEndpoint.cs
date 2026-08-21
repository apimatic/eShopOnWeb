using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — a logged-in shopper places an order from catalog items. It starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http, IOrderPaymentService service) =>
            {
                var buyerId = http.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var lines = (request.Items ?? new()).Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
                var address = request.ShipToAddress is null
                    ? null
                    : new ShippingAddressInput(
                        request.ShipToAddress.Street, request.ShipToAddress.City, request.ShipToAddress.State,
                        request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

                var result = await service.PlaceOrderAsync(buyerId, lines, address, http.RequestAborted);
                return result.ToApiResult(placed => Results.Created($"api/orders/{placed.OrderId}", placed));
            })
            .Produces<OrderPlaced>(StatusCodes.Status201Created)
            .WithTags("PaymentOrderEndpoints");
    }
}
