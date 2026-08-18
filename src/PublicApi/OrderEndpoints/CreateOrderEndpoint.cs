using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing order model. The shopper is then told their order was placed; a message that cannot be sent
/// never fails the placement.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IOrderService _orderService;
    private readonly IOrderNotificationService _notificationService;
    private readonly IAppLogger<CreateOrderEndpoint> _logger;

    public CreateOrderEndpoint(IOrderService orderService, IOrderNotificationService notificationService, IAppLogger<CreateOrderEndpoint> logger)
    {
        _orderService = orderService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest(new { error = "At least one order item is required." });

        var address = BuildAddress(request.ShipToAddress);
        var items = request.Items.Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity));

        Order order;
        try
        {
            order = await _orderService.CreateOrderAsync(buyerId, items, address);
        }
        catch (CatalogItemNotFoundException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        // Best effort: the order is placed regardless of whether the shopper can be messaged.
        try
        {
            await _notificationService.NotifyOrderPlacedAsync(order);
        }
        catch (System.Exception)
        {
            _logger.LogWarning("Order {OrderId} placed but the placed-notification step failed.", order.Id);
        }

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Items = order.OrderItems.Select(oi => new OrderItemDto
            {
                CatalogItemId = oi.ItemOrdered.CatalogItemId,
                ProductName = oi.ItemOrdered.ProductName,
                UnitPrice = oi.UnitPrice,
                Units = oi.Units
            }).ToList()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(ShippingAddressDto? dto)
    {
        if (dto is null)
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");

        return new Address(
            string.IsNullOrWhiteSpace(dto.Street) ? "N/A" : dto.Street,
            string.IsNullOrWhiteSpace(dto.City) ? "N/A" : dto.City,
            dto.State ?? "N/A",
            string.IsNullOrWhiteSpace(dto.Country) ? "N/A" : dto.Country,
            string.IsNullOrWhiteSpace(dto.ZipCode) ? "00000" : dto.ZipCode);
    }
}
