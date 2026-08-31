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
/// Places an order from catalog items for the authenticated shopper, reusing the app's existing
/// Order/OrderItem model. The buyer identity comes from the token, never the request body.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                ClaimsPrincipal user,
                IRepository<Order> orderRepository,
                IReadRepository<CatalogItem> itemRepository,
                IUriComposer uriComposer) =>
            {
                return await HandleAsync(request, user, orderRepository, itemRepository, uriComposer);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        ClaimsPrincipal user,
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer)
    {
        var buyerId = user.GetBuyerId();
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
            return Results.BadRequest("Every item quantity must be greater than zero.");
        }

        var catalogItemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds));

        var missing = catalogItemIds.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (missing.Length > 0)
        {
            return Results.BadRequest($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = request.Items.Select(requested =>
        {
            var catalogItem = catalogItems.First(c => c.Id == requested.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity);
        }).ToList();

        var order = new Order(buyerId, ToAddress(request.ShipToAddress), orderItems);
        await orderRepository.AddAsync(order);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Total = order.Total(),
            Items = order.OrderItems.Select(oi => new CreateOrderResponseItem
            {
                CatalogItemId = oi.ItemOrdered.CatalogItemId,
                ProductName = oi.ItemOrdered.ProductName,
                UnitPrice = oi.UnitPrice,
                Units = oi.Units
            }).ToList()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address ToAddress(AddressDto? dto)
    {
        // Shipping is not the concern of this billing flow; a sensible default is used when the
        // caller does not supply an address, mirroring the storefront checkout.
        return new Address(
            street: string.IsNullOrWhiteSpace(dto?.Street) ? "123 Main St." : dto!.Street!,
            city: string.IsNullOrWhiteSpace(dto?.City) ? "Kent" : dto!.City!,
            state: string.IsNullOrWhiteSpace(dto?.State) ? "OH" : dto!.State!,
            country: string.IsNullOrWhiteSpace(dto?.Country) ? "United States" : dto!.Country!,
            zipcode: string.IsNullOrWhiteSpace(dto?.ZipCode) ? "44240" : dto!.ZipCode!);
    }
}
