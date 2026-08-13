using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing Order/OrderItem model. The shopper is told their order was placed. Returns the new order id.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                ClaimsPrincipal user,
                IApiOrderService orderService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                if (request?.Items == null || request.Items.Count == 0)
                {
                    return Results.BadRequest(new { message = "An order must contain at least one item." });
                }

                var lines = request.Items
                    .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
                    .ToList();

                var address = ToAddress(request.ShipToAddress);

                // Unknown items / empty orders surface as OrderCreationException -> 400 via the exception middleware.
                var orderId = await orderService.PlaceOrderAsync(buyerId, lines, address, cancellationToken);

                return Results.Created($"api/orders/{orderId}", new { orderId, status = OrderStatus.Placed.ToString() });
            })
            .WithTags("OrderEndpoints");
    }

    private static Address ToAddress(ShippingAddressDto? dto)
    {
        if (dto == null)
        {
            // The app's original API/basket flow uses a placeholder address for orders without one.
            return new Address("123 Main St.", "Kent", "OH", "USA", "44240");
        }

        return new Address(
            string.IsNullOrWhiteSpace(dto.Street) ? "123 Main St." : dto.Street,
            string.IsNullOrWhiteSpace(dto.City) ? "Kent" : dto.City,
            string.IsNullOrWhiteSpace(dto.State) ? "OH" : dto.State,
            string.IsNullOrWhiteSpace(dto.Country) ? "USA" : dto.Country,
            string.IsNullOrWhiteSpace(dto.ZipCode) ? "44240" : dto.ZipCode);
    }
}
