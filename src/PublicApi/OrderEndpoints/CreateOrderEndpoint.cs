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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items. The order starts awaiting payment.
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
            (CreateOrderRequest request, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, cancellationToken);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateOrderCommand(
            request.Items.Select(item => new CreateOrderItem(item.CatalogItemId, item.Quantity)).ToList(),
            request.Street,
            request.City,
            request.State,
            request.Country,
            request.ZipCode);

        var order = await _orderPaymentService.CreateOrderAsync(
            user.Identity!.Name!, command, cancellationToken);

        return Results.Created($"api/orders/{order.OrderId}",
            OrderResponse.From(order, request.CorrelationId()));
    }
}
