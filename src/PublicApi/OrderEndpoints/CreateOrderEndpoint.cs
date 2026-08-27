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
/// Places an order from catalog items for the signed-in shopper and notifies
/// them by SMS. A notification that cannot be sent never fails the order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IOrderNotificationService _notificationService;
    private readonly IUriComposer _uriComposer;

    public CreateOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IOrderNotificationService notificationService,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _notificationService = notificationService;
        _uriComposer = uriComposer;
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

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one item is required." });
        }

        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { error = "Quantities must be positive." });
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()));

        var missingIds = request.Items.Select(i => i.CatalogItemId).Distinct()
            .Except(catalogItems.Select(c => c.Id)).ToList();
        if (missingIds.Count > 0)
        {
            return Results.BadRequest(new { error = $"Unknown catalog item ids: {string.Join(", ", missingIds)}" });
        }

        var orderItems = request.Items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var shipTo = new Address(
            request.Street ?? "123 Main St.",
            request.City ?? "Kent",
            request.State ?? "WA",
            request.Country ?? "USA",
            request.ZipCode ?? "98031");

        var order = new Order(buyerId, shipTo, orderItems);
        order = await _orderRepository.AddAsync(order);

        // Best-effort: messaging problems must never fail the order.
        await _notificationService.NotifyOrderPlacedAsync(order);

        return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        });
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
