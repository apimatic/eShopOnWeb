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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities. The order starts
/// awaiting payment. Unit prices come from the catalog, not the caller.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    // Placeholder ship-to used when the caller doesn't supply one (this API is payment-focused).
    private static readonly Address DefaultAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                PlaceOrderRequest request,
                ClaimsPrincipal user,
                IOrderService orderService,
                CancellationToken ct) =>
            {
                var buyerId = user.BuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var address = request.ShipToAddress is { } a
                    ? new Address(a.Street, a.City, a.State, a.Country, a.ZipCode)
                    : DefaultAddress;

                var items = request.Items
                    .Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity))
                    .ToList();

                var order = await orderService.CreateOrderAsync(buyerId, items, address, ct);

                var response = new PlaceOrderResponse
                {
                    OrderId = order.Id,
                    Total = order.Total(),
                    Status = "AwaitingPayment",
                    Items = order.ToLineDtos()
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
