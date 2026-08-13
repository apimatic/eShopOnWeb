using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog items, reusing the app's order/order-item model.
/// The shopper is told, by SMS, that their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest>
{
    private readonly IOrderNotificationService _service;

    public CreateOrderEndpoint(IOrderNotificationService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.CallerId = user.GetUserId();
                return await HandleAsync(request, ct);
            })
            .Produces<CreateOrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request) => HandleAsync(request, default);

    public async Task<IResult> HandleAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var response = new CreateOrderResponse(request.CorrelationId());
        if (string.IsNullOrEmpty(request.CallerId)) return Results.Unauthorized();
        if (request.Items is null || request.Items.Count == 0)
            return Results.BadRequest("An order must contain at least one item.");

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var order = await _service.PlaceOrderAsync(request.CallerId, lines, request.ShipToAddress?.ToAddress(), ct);

        response.OrderId = order.Id;
        response.Order = OrderSummaryDto.From(order);
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
