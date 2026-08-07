using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog items. The order is created awaiting payment
/// and can then be paid via <c>POST /api/orders/{orderId}/pay</c>.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;

    // Placeholder shipping address values used when the caller doesn't supply one - the storefront
    // checkout does the same. Ordering here is about exercising the payment flow, not shipping. A fresh
    // Address instance is created per order (owned entities must not be shared across aggregates).
    private const string DefaultStreet = "123 Main St.";
    private const string DefaultCity = "Kent";
    private const string DefaultState = "OH";
    private const string DefaultCountry = "USA";
    private const string DefaultZip = "44240";

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
            (CreateOrderRequest request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();

        if (request.Items == null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "Every item quantity must be greater than zero." });
        }

        // Collapse duplicate lines so quantities add up.
        var requestedQuantities = request.Items
            .GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(requestedQuantities.Keys.ToArray()));

        var missing = requestedQuantities.Keys.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            return Results.BadRequest(new { message = $"Unknown catalog item id(s): {string.Join(", ", missing)}." });
        }

        var orderItems = requestedQuantities.Select(kvp =>
        {
            var catalogItem = catalogItems.First(c => c.Id == kvp.Key);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        var order = new Order(buyerId, MapAddress(request.ShipToAddress), orderItems);
        await _orderRepository.AddAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = order.ToDto()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address MapAddress(ShipToAddressRequest? address)
    {
        return new Address(
            street: string.IsNullOrWhiteSpace(address?.Street) ? DefaultStreet : address!.Street!,
            city: string.IsNullOrWhiteSpace(address?.City) ? DefaultCity : address!.City!,
            state: string.IsNullOrWhiteSpace(address?.State) ? DefaultState : address!.State!,
            country: string.IsNullOrWhiteSpace(address?.Country) ? DefaultCountry : address!.Country!,
            zipcode: string.IsNullOrWhiteSpace(address?.ZipCode) ? DefaultZip : address!.ZipCode!);
    }
}
