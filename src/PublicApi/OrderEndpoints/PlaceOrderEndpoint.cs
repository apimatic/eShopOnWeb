using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders — place an order from catalog item ids and quantities, reusing the existing Order/OrderItem
/// model. The buyer is the token's caller. On success the shopper is told their order was placed (best-effort;
/// a messaging failure never fails the order).
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderService>
{
    private readonly IOrderNotificationService _notifications;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PlaceOrderEndpoint(IOrderNotificationService notifications, IHttpContextAccessor httpContextAccessor)
    {
        _notifications = notifications;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderService orderService) => await HandleAsync(request, orderService))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderService orderService)
    {
        var buyerId = EndpointUser.Name(_httpContextAccessor);
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        if (request.Items.Any(i => i.Quantity <= 0))
            return Results.BadRequest(new { message = "Every item quantity must be greater than zero." });

        var address = BuildAddress(request.ShipToAddress);
        var items = request.Items
            .Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await orderService.CreateOrderAsync(buyerId, address, items);

        await _notifications.NotifyOrderPlacedAsync(order, CancellationToken.None);

        return Results.Created($"api/orders/{order.Id}", new PlaceOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        });
    }

    private static Address BuildAddress(ShipToAddressDto? dto)
    {
        if (dto is null ||
            string.IsNullOrWhiteSpace(dto.Street) || string.IsNullOrWhiteSpace(dto.City) ||
            string.IsNullOrWhiteSpace(dto.Country) || string.IsNullOrWhiteSpace(dto.ZipCode))
        {
            // The Order aggregate requires a ship-to address; use a placeholder when none is supplied.
            return new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        }

        return new Address(dto.Street, dto.City, dto.State ?? "N/A", dto.Country, dto.ZipCode);
    }
}
