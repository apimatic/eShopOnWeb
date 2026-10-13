using System.Collections.Generic;
using System.Linq;
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
/// Places an order for a set of catalog items and quantities, reusing the app's existing order
/// aggregate. The ordered quantities are taken off eShop's own stock; an order for more of an
/// item than eShop holds is refused. Available to any signed-in shopper. This does not call
/// Walmart — Walmart learns the reduced levels at the next sync.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<CatalogItem>, IRepository<Order>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUriComposer _uriComposer;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor, IUriComposer uriComposer)
    {
        _httpContextAccessor = httpContextAccessor;
        _uriComposer = uriComposer;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IRepository<CatalogItem> itemRepository, IRepository<Order> orderRepository) =>
            {
                return await HandleAsync(request, itemRepository, orderRepository);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request,
        IRepository<CatalogItem> itemRepository,
        IRepository<Order> orderRepository)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }

        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest("Every item quantity must be greater than zero.");
        }

        var buyerId = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        // Combine duplicate lines so stock is checked and decremented against the true total.
        var requestedByItem = request.Items
            .GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var catalogItemsSpec = new CatalogItemsSpecification(requestedByItem.Keys.ToArray());
        var catalogItems = await itemRepository.ListAsync(catalogItemsSpec);

        var missing = requestedByItem.Keys.Where(id => catalogItems.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
        {
            return Results.BadRequest($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var catalogItem in catalogItems)
        {
            var units = requestedByItem[catalogItem.Id];
            // Throws InsufficientStockException (-> 400) if eShop holds fewer than requested.
            catalogItem.RemoveStock(units);

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, units));
        }

        // The stock capability does not collect a shipping address; the order aggregate requires
        // one, so record a placeholder. This can be extended if a real address is ever supplied.
        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        var order = new Order(buyerId, shipToAddress, orderItems);

        await orderRepository.AddAsync(order);
        await itemRepository.UpdateRangeAsync(catalogItems);

        response.OrderId = order.Id;
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
