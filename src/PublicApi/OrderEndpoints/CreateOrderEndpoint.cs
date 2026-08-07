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
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order is created
/// AwaitingPayment; prices come from the catalog and the caller's identity comes from the token.
/// Reuses the app's existing Order / OrderItem aggregate.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    // eShopOnWeb has no per-shopper shipping address; the storefront uses a fixed demo address too.
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IUriComposer _uriComposer;

    public CreateOrderEndpoint(IUriComposer uriComposer)
    {
        _uriComposer = uriComposer;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<CreateOrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext http)
    {
        var buyerId = http.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest("Every item quantity must be greater than zero.");
        }

        var itemRepository = http.RequestServices.GetRequiredService<IRepository<CatalogItem>>();
        var orderRepository = http.RequestServices.GetRequiredService<IRepository<Order>>();

        var requestedIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await itemRepository.ListAsync(new CatalogItemsSpecification(requestedIds));

        var missing = requestedIds.Where(id => catalogItems.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
        {
            return Results.BadRequest($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var shipToAddress = request.ShipToAddress is null
            ? DefaultShipToAddress
            : new Address(
                request.ShipToAddress.Street,
                request.ShipToAddress.City,
                request.ShipToAddress.State,
                request.ShipToAddress.Country,
                request.ShipToAddress.ZipCode);

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await orderRepository.AddAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDto.FromEntity(order)
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
