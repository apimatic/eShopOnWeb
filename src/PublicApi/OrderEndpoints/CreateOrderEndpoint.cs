using System.Collections.Generic;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper and notifies them by SMS.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notificationService;

    public CreateOrderEndpoint(IRepository<CatalogItem> catalogItemRepository,
        IRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        _catalogItemRepository = catalogItemRepository;
        _orderRepository = orderRepository;
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
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var items = new List<OrderItem>();
        foreach (var requestedItem in request.Items)
        {
            var catalogItem = await _catalogItemRepository.GetByIdAsync(requestedItem.CatalogItemId);
            if (catalogItem is null)
            {
                return Results.BadRequest($"Catalog item {requestedItem.CatalogItemId} does not exist.");
            }

            items.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                requestedItem.Quantity));
        }

        var address = new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
            request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = new Order(buyerId, address, items);
        order = await _orderRepository.AddAsync(order);

        // Never fails the order: messaging problems are recorded on the notification records.
        await _notificationService.NotifyOrderPlacedAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
