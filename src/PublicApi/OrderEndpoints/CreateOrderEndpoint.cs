using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Helpers;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper.
/// The order starts in AwaitingPayment state.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly PayPalSettings _payPalSettings;

    public CreateOrderEndpoint(IRepository<CatalogItem> itemRepository, IRepository<Order> orderRepository, IOptions<PayPalSettings> payPalSettings)
    {
        _itemRepository = itemRepository;
        _orderRepository = orderRepository;
        _payPalSettings = payPalSettings.Value;
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
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "At least one item with a positive quantity is required." });
        }

        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()));
        if (catalogItems.Count != request.Items.Select(i => i.CatalogItemId).Distinct().Count())
        {
            return Results.BadRequest(new { message = "One or more catalog items do not exist." });
        }

        var orderItems = request.Items
            .Select(i =>
            {
                var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
                var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
                return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
            })
            .ToList();

        var address = new Address(
            request.Street ?? "123 Main St.",
            request.City ?? "Kent",
            request.State ?? "OH",
            request.Country ?? "United States",
            request.ZipCode ?? "44240");

        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = _payPalSettings.Currency,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Units { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
}
