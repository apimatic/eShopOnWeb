using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemRequest> Items { get; set; } = new();
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(System.Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// POST /api/orders — places an order for the signed-in shopper from catalog item ids and
/// quantities, reusing the existing order model, then tells the shopper the order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orderService = http.RequestServices.GetRequiredService<IApiOrderService>();
        var lines = (request.Items ?? new List<OrderItemRequest>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        try
        {
            var order = await orderService.PlaceOrderAsync(buyerId, lines, http.RequestAborted);
            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Order = OrderDto.From(order)
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (InvalidOrderRequestException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
