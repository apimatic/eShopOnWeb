using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders — places an order from catalog item ids + quantities for the signed-in shopper,
/// reusing the app's existing Order/OrderItem model. The shopper is told their order was placed.
/// Returns the new id as top-level <c>orderId</c>.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext, IRepository<Order>>
{
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IReadRepository<OrderNotification> _notificationsRead;
    private readonly IOrderNotificationService _orderNotifications;

    public CreateOrderEndpoint(
        IUriComposer uriComposer,
        IRepository<CatalogItem> catalogItems,
        IReadRepository<OrderNotification> notificationsRead,
        IOrderNotificationService orderNotifications)
    {
        _uriComposer = uriComposer;
        _catalogItems = catalogItems;
        _notificationsRead = notificationsRead;
        _orderNotifications = orderNotifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(request, http, orderRepository);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext http, IRepository<Order> orderRepository)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { error = "An order must contain at least one item." });
        }
        if (request.Items.Any(i => i.Quantity < 1))
        {
            return Results.BadRequest(new { error = "Every item must have a quantity of at least 1." });
        }

        var ids = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), http.RequestAborted);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            return Results.BadRequest(new { error = $"Unknown catalog item id(s): {string.Join(", ", missing)}." });
        }

        var orderItems = request.Items.Select(line =>
        {
            var item = byId[line.CatalogItemId];
            var ordered = new CatalogItemOrdered(item.Id, item.Name, _uriComposer.ComposePicUri(item.PictureUri));
            return new OrderItem(ordered, item.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, CreatePlaceholderShippingAddress(), orderItems);
        await orderRepository.AddAsync(order, http.RequestAborted);

        // Best-effort: a message that cannot be sent must not fail order placement.
        await _orderNotifications.NotifyOrderPlacedAsync(order, http.RequestAborted);

        var notifications = await _notificationsRead.ListAsync(new OrderNotificationsByOrderSpecification(order.Id), http.RequestAborted);
        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Notifications = notifications.Select(OrderNotificationDto.From).ToList()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    // The notification API does not collect a shipping address; the existing Order model requires one,
    // so a clearly-marked placeholder is used. (Shipping-address capture is out of scope here.)
    private static Address CreatePlaceholderShippingAddress() =>
        new("N/A", "N/A", "N/A", "N/A", "00000");
}
