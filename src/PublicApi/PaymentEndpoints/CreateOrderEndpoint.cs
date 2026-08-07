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
/// POST api/orders — places an order from catalog items for the signed-in shopper. The order
/// starts awaiting payment. Returns the new order's identifier as a top-level <c>orderId</c>.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService orderPaymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var lines = (request.Items ?? new())
                    .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
                    .ToList();

                var order = await orderPaymentService.PlaceOrderAsync(buyerId, lines, BuildAddress(request.ShipToAddress), cancellationToken);

                var response = new CreateOrderResponse
                {
                    OrderId = order.Id,
                    PaymentStatus = order.PaymentStatus.ToString(),
                    Total = order.Total(),
                    Currency = OrderMapping.Currency,
                    OrderDate = order.OrderDate,
                    Items = order.ToItemDtos().ToList()
                };

                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    private static Address BuildAddress(ShippingAddressDto? dto)
    {
        // Ship-to is optional on the API; use a placeholder when absent so the existing order model
        // (which requires an address) is satisfied without inventing a parallel order type.
        return new Address(
            street: NullIfBlank(dto?.Street) ?? "N/A",
            city: NullIfBlank(dto?.City) ?? "N/A",
            state: dto?.State ?? string.Empty,
            country: NullIfBlank(dto?.Country) ?? "N/A",
            zipcode: NullIfBlank(dto?.ZipCode) ?? "00000");
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
