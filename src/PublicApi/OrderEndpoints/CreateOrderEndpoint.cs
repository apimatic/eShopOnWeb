using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationService _notificationService;

    public CreateOrderEndpoint(
        IHttpContextAccessor httpContextAccessor,
        IOrderNotificationService notificationService)
    {
        _httpContextAccessor = httpContextAccessor;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderService orderService) =>
            {
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService)
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is not available.");
        var buyerId = httpContext.GetRequiredBuyerId();

        if (request.Items == null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        }

        var quantities = request.Items
            .Select(i => new CatalogQuantity(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await orderService.CreateOrderFromCatalogItemsAsync(buyerId, quantities);
        await _notificationService.TryNotifyOrderPlacedAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
