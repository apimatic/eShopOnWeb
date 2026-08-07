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
/// Places an order for the signed-in shopper from catalog items. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public CreateOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, ct);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var lines = (request.Items ?? new()).Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();

        var order = await _orderPaymentService.PlaceOrderAsync(buyerId, lines, ct);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = order.ToDto()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
