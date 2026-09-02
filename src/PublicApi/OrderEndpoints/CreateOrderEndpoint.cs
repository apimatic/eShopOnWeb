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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper.
/// The order starts in AwaitingPayment status.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _payPalSettings;

    public CreateOrderEndpoint(
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository,
        IUriComposer uriComposer,
        PayPalSettings payPalSettings)
    {
        _itemRepository = itemRepository;
        _orderRepository = orderRepository;
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

        if (request.Items == null || request.Items.Count == 0)
        {
            return Results.BadRequest("The order must contain at least one item.");
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest("Item quantities must be positive.");
        }

        var catalogItemsSpecification = new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification);

        var missingIds = request.Items.Select(i => i.CatalogItemId).Except(catalogItems.Select(c => c.Id)).ToList();
        if (missingIds.Count > 0)
        {
            return Results.BadRequest($"Unknown catalog item ids: {string.Join(", ", missingIds)}.");
        }

        var items = request.Items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var address = new Address(
            string.IsNullOrWhiteSpace(request.ShipToStreet) ? "One Microsoft Way" : request.ShipToStreet,
            string.IsNullOrWhiteSpace(request.ShipToCity) ? "Redmond" : request.ShipToCity,
            string.IsNullOrWhiteSpace(request.ShipToState) ? "WA" : request.ShipToState,
            string.IsNullOrWhiteSpace(request.ShipToCountry) ? "USA" : request.ShipToCountry,
            string.IsNullOrWhiteSpace(request.ShipToZipCode) ? "98052" : request.ShipToZipCode);

        var order = new Order(buyerId, address, items);
        order = await _orderRepository.AddAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = _payPalSettings.Currency
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
