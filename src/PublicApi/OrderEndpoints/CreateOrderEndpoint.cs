using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper and notifies them by SMS.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IOrderService _orderService;
    private readonly IOrderNotificationService _notificationService;

    public CreateOrderEndpoint(IOrderService orderService, IOrderNotificationService notificationService)
    {
        _orderService = orderService;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user)
    {
        if (request.Items.Count == 0)
        {
            throw new InvalidOrderRequestException("An order must contain at least one item.");
        }

        var buyerId = user.Identity!.Name!;
        var address = request.ShipToAddress is null
            ? new Address("123 Main Street", "Kent", "OH", "United States", "44240")
            : new Address(request.ShipToAddress.Street, request.ShipToAddress.City, request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var items = request.Items.Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity)).ToList();
        var order = await _orderService.CreateOrderAsync(buyerId, address, items);

        await _notificationService.NotifyOrderPlacedAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
