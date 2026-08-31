using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items. The order starts awaiting payment;
/// pay for it with POST api/orders/{orderId}/pay.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    // A fresh instance per order: reusing one Address object across orders makes EF try to
    // re-key the same owned entity instance, which throws once the store has prior orders.
    private static Address CreateDefaultAddress() => new("123 Main St", "Kent", "WA", "United States", "98032");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _payPalSettings;

    public CreateOrderEndpoint(IRepository<Order> orderRepository, IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer, PayPalSettings payPalSettings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _payPalSettings = payPalSettings;
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
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var response = new CreateOrderResponse(request.CorrelationId());

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentDomainException("An order must contain at least one item.");
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            throw new PaymentDomainException("Item quantities must be greater than zero.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()));

        var orderItems = request.Items.Select(item =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId)
                ?? throw new NotFoundException($"Catalog item {item.CatalogItemId} was not found.");
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var shipTo = request.ShipToAddress is null
            ? CreateDefaultAddress()
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State ?? string.Empty, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = new Order(buyerId, shipTo, orderItems);
        order = await _orderRepository.AddAsync(order);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.Total = order.Total();
        response.Currency = _payPalSettings.Currency;
        response.OrderDate = order.OrderDate;
        response.Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList();

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
