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
/// Places an order from catalog items for the authenticated shopper. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPaymentService orderPaymentService, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                await HandleAsync(request, orderPaymentService, user, cancellationToken))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        CreateOrderRequest request,
        IOrderPaymentService orderPaymentService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var lines = (request.Items ?? new())
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
            .ToList();

        var result = await orderPaymentService.PlaceOrderAsync(buyerId, lines, cancellationToken);

        switch (result.Outcome)
        {
            case PlaceOrderOutcome.Placed:
                var response = new CreateOrderResponse(request.CorrelationId())
                {
                    OrderId = result.Order!.Id,
                    Order = OrderDto.FromOrder(result.Order!)
                };
                return Results.Created($"api/orders/{response.OrderId}", response);

            case PlaceOrderOutcome.EmptyOrder:
            case PlaceOrderOutcome.CatalogItemNotFound:
                return Results.Problem(detail: result.Error, statusCode: StatusCodes.Status400BadRequest);

            default:
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
