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
using Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the authenticated shopper from catalog item ids and quantities, reusing the
/// app's existing Order / OrderItem model. This exists so the whole invoicing flow can be driven
/// through PublicApi alone (the in-memory store is per-host, so orders placed on the Web storefront
/// are not visible here).
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    public CreateOrderEndpoint(
        IRepository<Order> orderRepository,
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
            (CreateOrderRequest request, HttpContext context) =>
            {
                return await HandleAsync(request, context.User);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.GetCallerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }

        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest("Every order item must have a quantity of at least one.");
        }

        var itemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds));
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = itemIds.Where(id => !catalogById.ContainsKey(id)).ToList();
        if (missing.Count != 0)
        {
            return Results.BadRequest($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = request.Items.Select(item =>
        {
            var catalogItem = catalogById[item.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var address = BuildAddress(request.ShipToAddress);
        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Total = order.Total(),
            Items = order.OrderItems.Select(oi => new CreateOrderItemResponse
            {
                CatalogItemId = oi.ItemOrdered.CatalogItemId,
                ProductName = oi.ItemOrdered.ProductName,
                UnitPrice = oi.UnitPrice,
                Quantity = oi.Units
            }).ToList()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(ShipToAddressRequest? request)
    {
        if (request is null)
        {
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");
        }

        return new Address(
            Coalesce(request.Street),
            Coalesce(request.City),
            request.State ?? "N/A",
            Coalesce(request.Country),
            Coalesce(request.ZipCode));
    }

    private static string Coalesce(string? value) => string.IsNullOrWhiteSpace(value) ? "N/A" : value;
}
