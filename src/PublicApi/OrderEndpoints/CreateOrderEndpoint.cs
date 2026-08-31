using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper and tells them
/// by text message. A messaging failure never fails the order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    private static readonly Address DefaultAddress = new("1 eShop Way", "Redmond", "WA", "US", "98052");

    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notifications;
    private readonly IUriComposer _uriComposer;

    public CreateOrderEndpoint(
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IOrderNotificationService notifications,
        IUriComposer uriComposer)
    {
        _itemRepository = itemRepository;
        _orderRepository = orderRepository;
        _notifications = notifications;
        _uriComposer = uriComposer;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }
        if (request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order needs at least one item." });
        }
        if (request.Items.Any(i => i.Units <= 0))
        {
            return Results.BadRequest(new { message = "Every item needs a quantity of at least one." });
        }

        var requestedIds = request.Items.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(requestedIds), httpContext.RequestAborted);
        if (catalogItems.Count != requestedIds.Distinct().Count())
        {
            var known = catalogItems.Select(c => c.Id).ToHashSet();
            var unknown = requestedIds.Distinct().Where(id => !known.Contains(id));
            return Results.BadRequest(new { message = $"Unknown catalog item id(s): {string.Join(", ", unknown)}." });
        }

        var orderItems = request.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(group =>
            {
                var catalogItem = catalogItems.First(c => c.Id == group.Key);
                var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                    _uriComposer.ComposePicUri(catalogItem.PictureUri));
                return new OrderItem(itemOrdered, catalogItem.Price, group.Sum(i => i.Units));
            })
            .ToList();

        var address = request.ShipToAddress is null
            ? DefaultAddress
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City, request.ShipToAddress.State,
                request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, httpContext.RequestAborted);

        // Tells the shopper their order was placed; never fails the order itself.
        await _notifications.NotifyOrderPlacedAsync(order, httpContext.RequestAborted);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total()
        };
        response.Items.AddRange(order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }));

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
