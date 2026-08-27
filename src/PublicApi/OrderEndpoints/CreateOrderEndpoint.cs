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
/// Places an order from catalog items for the signed-in shopper and notifies
/// the shopper by SMS that the order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    private static readonly Address DefaultShipToAddress = new Address("1 eShop Way", "Bellevue", "WA", "USA", "98004");

    private readonly IUriComposer _uriComposer;

    public CreateOrderEndpoint(IUriComposer uriComposer)
    {
        _uriComposer = uriComposer;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IRepository<Order> orderRepository,
                IRepository<CatalogItem> itemRepository, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(request, user, orderRepository, itemRepository, notificationService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository, IOrderNotificationService notificationService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (request.Items == null || request.Items.Count == 0 || request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "The order must contain at least one item with a positive quantity." });
        }

        var catalogItems = await itemRepository.ListAsync(new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()));
        var missingIds = request.Items.Select(i => i.CatalogItemId).Distinct().Except(catalogItems.Select(c => c.Id)).ToList();
        if (missingIds.Count > 0)
        {
            return Results.BadRequest(new { message = $"Unknown catalog item id(s): {string.Join(", ", missingIds)}." });
        }

        var orderItems = request.Items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var shipTo = request.ShipToAddress == null
            ? DefaultShipToAddress
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City, request.ShipToAddress.State ?? string.Empty, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = new Order(buyerId, shipTo, orderItems);
        order = await orderRepository.AddAsync(order);

        // Best-effort: a messaging failure never fails the order.
        await notificationService.NotifyOrderPlacedAsync(order);

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
