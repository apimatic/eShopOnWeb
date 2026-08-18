using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the
/// app's existing order/order-item model. The shopper is then told their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public CreateOrderEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

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
        var buyerId = http.User.GetUserName();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest("At least one order item is required.");

        if (request.Items.Any(i => i.Quantity <= 0))
            return Results.BadRequest("Every order item must have a quantity of at least 1.");

        var lines = request.Items
            .Select(i => new OrderLineSelection(i.CatalogItemId, i.Quantity))
            .ToList();

        try
        {
            var order = await _orderNotificationService.PlaceOrderAsync(buyerId, lines, http.RequestAborted);

            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (CatalogItemNotFoundException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }
}
