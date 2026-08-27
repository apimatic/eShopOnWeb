using System;
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

/// <summary>
/// Places an order from catalog items for the signed-in shopper and notifies them by
/// SMS that the order was placed. A notification that cannot be sent never fails the
/// order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest>
{
    private static readonly Address DefaultAddress = new Address("To be confirmed", "To be confirmed", "", "To be confirmed", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogItemRepository;
    private readonly IOrderNotificationService _notificationService;

    public CreateOrderEndpoint(IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogItemRepository,
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
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request)
    {
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(i => i.Units <= 0))
        {
            return Results.BadRequest(new { message = "The order must contain at least one item with a positive quantity." });
        }

        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()));

        var missingIds = request.Items.Select(i => i.CatalogItemId)
            .Except(catalogItems.Select(c => c.Id)).ToList();
        if (missingIds.Count > 0)
        {
            return Results.BadRequest(new { message = $"Unknown catalog item id(s): {string.Join(", ", missingIds)}." });
        }

        var orderItems = request.Items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, i.Units);
        }).ToList();

        var address = request.ShipToAddress is null
            ? DefaultAddress
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = await _orderRepository.AddAsync(new Order(request.BuyerId, address, orderItems));

        await _notificationService.NotifyOrderPlacedAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new List<OrderItemRequest>();
    public ShipToAddressRequest? ShipToAddress { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class OrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Units { get; set; }
}

public class ShipToAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
}
