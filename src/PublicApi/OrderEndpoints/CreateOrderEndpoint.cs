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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    [Required]
    [MinLength(1, ErrorMessage = "At least one item is required.")]
    public List<CreateOrderItemRequest> Items { get; set; } = new();

    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; set; }

    [Range(1, 1000)]
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string? Currency { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper.
/// The order starts in AwaitingPayment state.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly PaymentSettings _paymentSettings;

    public CreateOrderEndpoint(
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer,
        IOptions<PaymentSettings> paymentSettings)
    {
        _catalogItemRepository = catalogItemRepository;
        _orderRepository = orderRepository;
        _uriComposer = uriComposer;
        _paymentSettings = paymentSettings.Value;
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
        var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        if (request.Items.Count == 0 || request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "At least one item with a positive quantity is required." });
        }

        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).Distinct().ToArray()));

        var missing = request.Items.Select(i => i.CatalogItemId).Distinct()
            .Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            return Results.BadRequest(new { message = $"Unknown catalog item id(s): {string.Join(", ", missing)}." });
        }

        var orderItems = request.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g =>
            {
                var catalogItem = catalogItems.First(c => c.Id == g.Key);
                var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
                return new OrderItem(itemOrdered, catalogItem.Price, g.Sum(i => i.Quantity));
            })
            .ToList();

        var shipTo = request.ShipToAddress is null
            ? new Address("123 Main St.", "Kent", "OH", "United States", "44240")
            : new Address(
                request.ShipToAddress.Street ?? string.Empty,
                request.ShipToAddress.City ?? string.Empty,
                request.ShipToAddress.State ?? string.Empty,
                request.ShipToAddress.Country ?? string.Empty,
                request.ShipToAddress.ZipCode ?? string.Empty);

        var order = new Order(buyerId, shipTo, orderItems, _paymentSettings.Currency);
        await _orderRepository.AddAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = order.Currency,
            Items = OrderDto.FromOrder(order).Items
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
