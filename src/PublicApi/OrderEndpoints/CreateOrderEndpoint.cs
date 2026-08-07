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
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting
/// payment; pay for it with <c>POST /api/orders/{orderId}/pay</c>.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Places an order from catalog items", "Creates an order awaiting payment for the signed-in shopper."));
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var lines = (request.Items ?? Enumerable.Empty<CreateOrderItem>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var shipToAddress = BuildAddress(request.ShipToAddress);

        var order = await orderPaymentService.CreateOrderAsync(request.BuyerId, lines, shipToAddress);

        response.OrderId = order.Id;
        response.Order = OrderDto.FromOrder(order);

        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address BuildAddress(ShipToAddressRequest? address)
    {
        // Payments, not shipping, are the focus here; a placeholder keeps the (required) address valid.
        return new Address(
            street: Fallback(address?.Street, "N/A"),
            city: Fallback(address?.City, "N/A"),
            state: Fallback(address?.State, "N/A"),
            country: Fallback(address?.Country, "US"),
            zipcode: Fallback(address?.ZipCode, "00000"));
    }

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
