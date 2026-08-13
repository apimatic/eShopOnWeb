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

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper (identity from the
/// token) using the app's existing order/order-item model, and tells the shopper it was placed.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                PlaceOrderRequest request,
                IOrderMessagingService service,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetOwnerId(user);

                var lines = (request.Items ?? new List<OrderLineRequest>())
                    .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
                    .ToList();

                var address = ToAddress(request.ShipToAddress);
                var order = await service.PlaceOrderAsync(buyerId, lines, address, cancellationToken);

                var response = new PlaceOrderResponse
                {
                    OrderId = order.Id,
                    Order = OrderDto.From(order)
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    private static Address ToAddress(ShippingAddressRequest? a)
    {
        if (a is null)
        {
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");
        }
        return new Address(
            string.IsNullOrWhiteSpace(a.Street) ? "N/A" : a.Street,
            string.IsNullOrWhiteSpace(a.City) ? "N/A" : a.City,
            string.IsNullOrWhiteSpace(a.State) ? "N/A" : a.State,
            string.IsNullOrWhiteSpace(a.Country) ? "N/A" : a.Country,
            string.IsNullOrWhiteSpace(a.ZipCode) ? "00000" : a.ZipCode);
    }
}
