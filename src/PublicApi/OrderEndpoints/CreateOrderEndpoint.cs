using System;
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
/// Places an order from catalog items. The order starts in PendingPayment,
/// awaiting payment via POST api/orders/{orderId}/pay.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    public CreateOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
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
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        }

        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "Item quantities must be positive." });
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()));

        var missing = request.Items.Select(i => i.CatalogItemId).Distinct()
            .Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            return Results.BadRequest(new { message = $"Unknown catalog item ids: {string.Join(", ", missing)}" });
        }

        var orderItems = request.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g =>
            {
                var catalogItem = catalogItems.First(c => c.Id == g.Key);
                var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                    _uriComposer.ComposePicUri(catalogItem.PictureUri));
                return new OrderItem(itemOrdered, catalogItem.Price, g.Sum(i => i.Quantity));
            })
            .ToList();

        var address = new Address(
            request.ShipToStreet ?? "N/A",
            request.ShipToCity ?? "N/A",
            request.ShipToState ?? string.Empty,
            request.ShipToCountry ?? "N/A",
            request.ShipToZipCode ?? "N/A");

        var order = new Order(request.BuyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Total = order.Total(),
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
