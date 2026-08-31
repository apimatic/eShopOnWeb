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
/// Places an order from catalog items for the signed-in shopper and notifies them by SMS.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    private readonly IUriComposer _uriComposer;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IOrderNotificationService _notificationService;

    public CreateOrderEndpoint(
        IUriComposer uriComposer,
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IOrderNotificationService notificationService)
    {
        _uriComposer = uriComposer;
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext httpContext)
    {
        var response = new CreateOrderResponse(request.CorrelationId());
        var cancellationToken = httpContext.RequestAborted;

        var requestedIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(requestedIds), cancellationToken);
        if (catalogItems.Count != requestedIds.Length)
        {
            return Results.BadRequest("One or more catalog items do not exist.");
        }

        var orderItems = request.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g =>
            {
                var catalogItem = catalogItems.First(c => c.Id == g.Key);
                var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
                return new OrderItem(itemOrdered, catalogItem.Price, g.Sum(i => i.Quantity));
            })
            .ToList();

        var address = new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
        var order = new Order(httpContext.User.Identity!.Name!, address, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        // Best-effort: a notification failure never fails the order.
        await _notificationService.NotifyOrderPlacedAsync(order, cancellationToken);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.Total = order.Total();
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
