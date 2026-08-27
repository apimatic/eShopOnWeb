using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
/// Places an order from catalog items for the signed-in shopper and tells them by text
/// message that the order was placed. A message that cannot be sent never fails the order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IOrderNotificationService _orderNotificationService;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<CreateOrderEndpoint> _logger;

    public CreateOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IOrderNotificationService orderNotificationService,
        IUriComposer uriComposer,
        IAppLogger<CreateOrderEndpoint> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _orderNotificationService = orderNotificationService;
        _uriComposer = uriComposer;
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
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "The order must contain at least one item with a positive quantity." });
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()));
        if (catalogItems.Count != request.Items.Select(i => i.CatalogItemId).Distinct().Count())
        {
            return Results.BadRequest(new { message = "One or more catalog items do not exist." });
        }

        var orderItems = request.Items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var address = new Address(request.ShipToStreet, request.ShipToCity, request.ShipToState,
            request.ShipToCountry, request.ShipToZipCode);
        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order);

        try
        {
            await _orderNotificationService.NotifyOrderPlacedAsync(order);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Order {order.Id} placed, but the confirmation message could not be recorded: {ex.Message}");
        }

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public class CreateOrderRequest : BaseRequest
{
    [Required]
    public List<CreateOrderItemRequest> Items { get; set; } = new();

    [Required]
    public string ShipToStreet { get; set; } = string.Empty;
    [Required]
    public string ShipToCity { get; set; } = string.Empty;
    public string ShipToState { get; set; } = string.Empty;
    [Required]
    public string ShipToCountry { get; set; } = string.Empty;
    [Required]
    public string ShipToZipCode { get; set; } = string.Empty;
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) {}
    public CreateOrderResponse() {}

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
