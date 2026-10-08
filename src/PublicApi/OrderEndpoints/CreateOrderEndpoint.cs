using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the caller from catalog items; the order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces<CreateOrderResponse>(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var lines = (request.Items ?? new()).Select(i => new PlaceOrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var address = request.ShipToAddress is { } a
            ? new Address(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : null;

        var result = await orderPaymentService.PlaceOrderAsync(request.BuyerId, lines, address);
        response.Message = result.Message;
        if (result.Outcome != PlaceOrderOutcome.Created || result.Order is null)
            return Results.BadRequest(response);

        response.OrderId = result.Order.Id;
        response.Order = OrderDto.From(result.Order);
        return Results.Created("api/my-orders", response);
    }
}
