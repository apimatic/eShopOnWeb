using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
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

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new List<OrderItemRequest>();
    public AddressRequest? ShipToAddress { get; set; }

    /// <summary>Populated from the JWT, never from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? State { get; set; }
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse() { }
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper and notifies
/// them by SMS if they have a number on file.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>>
{
    private readonly IReadRepository<CatalogItem> _catalogItemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notificationService;

    public CreateOrderEndpoint(
        IReadRepository<CatalogItem> catalogItemRepository,
        IUriComposer uriComposer,
        IOrderNotificationService notificationService)
    {
        _catalogItemRepository = catalogItemRepository;
        _uriComposer = uriComposer;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IRepository<Order> orderRepository) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, orderRepository);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> orderRepository)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest("Item quantities must be positive.");
        }

        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()));

        var orderItems = new List<OrderItem>();
        foreach (var itemRequest in request.Items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == itemRequest.CatalogItemId);
            if (catalogItem is null)
            {
                return Results.BadRequest($"Unknown catalog item id {itemRequest.CatalogItemId}.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, itemRequest.Quantity));
        }

        var address = request.ShipToAddress is null
            ? new Address("1 Microsoft Way", "Redmond", "WA", "USA", "98052")
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State ?? string.Empty, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = new Order(request.BuyerId, address, orderItems);
        order = await orderRepository.AddAsync(order);

        // Never fails the order: a messaging problem is recorded, not thrown.
        await _notificationService.NotifyOrderPlacedAsync(order);

        return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        });
    }
}
