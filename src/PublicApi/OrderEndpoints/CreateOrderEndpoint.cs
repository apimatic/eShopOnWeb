using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper.
/// The order starts in the AwaitingPayment state.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _payPalSettings;

    public CreateOrderEndpoint(IUriComposer uriComposer, IOptions<PayPalSettings> payPalSettings)
    {
        _uriComposer = uriComposer;
        _payPalSettings = payPalSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user,
                IRepository<Order> orderRepository, IRepository<CatalogItem> itemRepository) =>
            {
                return await HandleAsync(request, user, orderRepository, itemRepository);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user,
        IRepository<Order> orderRepository, IRepository<CatalogItem> itemRepository)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var response = new CreateOrderResponse(request.CorrelationId());

        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest("The order must contain at least one item with a positive quantity.");
        }

        var catalogItems = await itemRepository.ListAsync(
            new CatalogItemsSpecification(request.Items.Select(i => i.CatalogItemId).ToArray()));

        var missing = request.Items.Select(i => i.CatalogItemId)
            .Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            return Results.BadRequest($"Unknown catalog item ids: {string.Join(", ", missing)}.");
        }

        var items = request.Items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var address = new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
        var order = new Order(buyerId, address, items);
        await orderRepository.AddAsync(order);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.Total = order.Total();
        response.Currency = _payPalSettings.Currency;

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
