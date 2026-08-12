using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Security.Claims;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog item ids + quantities, reusing the app's existing order/order-item
/// model. The caller's identity (the BuyerId) comes from the token. On success the shopper is told,
/// by SMS, that their order was placed. A messaging failure never fails the order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IRepository<Order>>
{
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderNotificationService _notifications;

    public CreateOrderEndpoint(
        IRepository<CatalogItem> catalogItems,
        IUriComposer uriComposer,
        IOrderNotificationService notifications)
    {
        _catalogItems = catalogItems;
        _uriComposer = uriComposer;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IRepository<Order> orderRepository) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.AssignBuyer(buyerId);
                return await HandleAsync(request, orderRepository);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IRepository<Order> orderRepository)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { error = "An order must contain at least one item." });
        }

        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { error = "Every item quantity must be greater than zero." });
        }

        var requestedIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(requestedIds));

        var missing = requestedIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return Results.BadRequest(new { error = $"Unknown catalog item id(s): {string.Join(", ", missing)}." });
        }

        var orderItems = request.Items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = BuildAddress(request.ShipToAddress);
        var order = new Order(request.BuyerId, address, orderItems);
        order = await orderRepository.AddAsync(order);

        // Tell the shopper their order was placed. Best-effort: never fails the order.
        await _notifications.NotifyOrderPlacedAsync(order, CancellationToken.None);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(AddressDto? dto)
    {
        if (dto is null)
        {
            // The SMS feature does not require a shipping address; use a placeholder that satisfies
            // the existing order model when the caller does not supply one.
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");
        }

        return new Address(
            string.IsNullOrWhiteSpace(dto.Street) ? "N/A" : dto.Street,
            string.IsNullOrWhiteSpace(dto.City) ? "N/A" : dto.City,
            dto.State ?? string.Empty,
            string.IsNullOrWhiteSpace(dto.Country) ? "N/A" : dto.Country,
            string.IsNullOrWhiteSpace(dto.ZipCode) ? "00000" : dto.ZipCode);
    }
}
