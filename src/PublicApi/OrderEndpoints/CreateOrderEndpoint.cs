using System.Collections.Generic;
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
/// Places an order from catalog items for the signed-in shopper (identity comes from the
/// token). The shopper is told by SMS that their order was placed; a notification that
/// cannot be sent never fails the order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IOrderNotificationService _notificationService;

    public CreateOrderEndpoint(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IOrderNotificationService notificationService)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user)
    {
        var userName = user.GetUserName();
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one item is required." });
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "Every item quantity must be at least 1." });
        }

        var requestedIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToList();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsByIdsSpec(requestedIds));
        var catalogItemsById = catalogItems.ToDictionary(i => i.Id);
        var missingIds = requestedIds.Where(id => !catalogItemsById.ContainsKey(id)).ToList();
        if (missingIds.Count > 0)
        {
            return Results.BadRequest(new { message = $"Unknown catalog item id(s): {string.Join(", ", missingIds)}." });
        }

        var orderItems = new List<OrderItem>();
        foreach (var item in request.Items)
        {
            var catalogItem = catalogItemsById[item.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var shipTo = new Address(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State,
            request.ShipTo.Country, request.ShipTo.ZipCode);
        var order = new Order(userName, shipTo, orderItems);
        order = await _orderRepository.AddAsync(order);

        // Never fails the order: failures are recorded on the notification records.
        await _notificationService.NotifyOrderPlacedAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            OrderDate = order.OrderDate
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
