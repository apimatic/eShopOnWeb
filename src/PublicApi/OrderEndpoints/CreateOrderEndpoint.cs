using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public List<OrderItemDto> Items { get; set; } = new();
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order reuses the app's existing
/// Order/OrderItem model and starts awaiting payment. Amounts come from catalog prices (USD).
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ClaimsPrincipal user,
                   IRepository<Order> orderRepository, IReadRepository<CatalogItem> itemRepository,
                   IUriComposer uriComposer, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (request.Items == null || request.Items.Count == 0)
                {
                    return Results.BadRequest("An order must contain at least one item.");
                }

                if (request.Items.Any(i => i.Quantity <= 0))
                {
                    return Results.BadRequest("Every item quantity must be greater than zero.");
                }

                var itemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
                var catalogItems = await itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), ct);
                if (catalogItems.Count != itemIds.Length)
                {
                    return Results.BadRequest("One or more catalog items could not be found.");
                }

                var orderItems = request.Items.Select(requested =>
                {
                    var catalogItem = catalogItems.First(c => c.Id == requested.CatalogItemId);
                    var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                        uriComposer.ComposePicUri(catalogItem.PictureUri));
                    return new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity);
                }).ToList();

                var order = new Order(buyerId, BuildAddress(request.ShipToAddress), orderItems);
                order = await orderRepository.AddAsync(order, ct);

                var response = new CreateOrderResponse
                {
                    OrderId = order.Id,
                    PaymentStatus = order.PaymentStatus.ToString(),
                    Total = order.Total(),
                    Items = orderItems.Select(oi => new OrderItemDto
                    {
                        CatalogItemId = oi.ItemOrdered.CatalogItemId,
                        ProductName = oi.ItemOrdered.ProductName,
                        UnitPrice = oi.UnitPrice,
                        Units = oi.Units
                    }).ToList()
                };

                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Places an order from catalog items", "Creates an order for the signed-in shopper, awaiting payment."));
    }

    private static Address BuildAddress(ShippingAddressRequest? address)
    {
        // Shipping is out of scope for the payment feature; use provided values or a placeholder.
        return new Address(
            address?.Street ?? "N/A",
            address?.City ?? "N/A",
            address?.State ?? "N/A",
            address?.Country ?? "N/A",
            address?.ZipCode ?? "N/A");
    }
}
