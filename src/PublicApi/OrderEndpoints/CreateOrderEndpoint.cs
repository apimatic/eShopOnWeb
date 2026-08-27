using System;
using System.Linq;
using System.Security.Claims;
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
/// Places an order from catalog items for the signed-in shopper and notifies them by SMS.
/// A notification failure never fails the order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IOrderNotificationService _notificationService;
    private readonly IAppLogger<CreateOrderEndpoint> _logger;

    public CreateOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IOrderNotificationService notificationService,
        IAppLogger<CreateOrderEndpoint> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _notificationService = notificationService;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user)
    {
        if (request.Items.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one item is required." });
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()));

        var missing = request.Items.Select(i => i.CatalogItemId).Distinct()
            .Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            return Results.BadRequest(new { error = $"Unknown catalog item ids: {string.Join(", ", missing)}" });
        }

        var orderItems = request.Items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var address = new Address(
            request.ShipToStreet ?? "1 Main St",
            request.ShipToCity ?? "Seattle",
            request.ShipToState ?? "WA",
            request.ShipToCountry ?? "US",
            request.ShipToZipCode ?? "98101");

        var order = new Order(user.Identity!.Name!, address, orderItems);
        order = await _orderRepository.AddAsync(order);

        await NotifySafely(() => _notificationService.NotifyOrderPlacedAsync(order), order.Id);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private async Task NotifySafely(Func<Task> notify, int orderId)
    {
        try
        {
            await notify();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Notification for order {OrderId} failed; the order operation succeeded: {Error}", orderId, ex.Message);
        }
    }
}
