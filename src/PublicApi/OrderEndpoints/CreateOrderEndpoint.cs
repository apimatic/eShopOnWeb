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
/// Places an order from catalog items for the signed-in shopper. The order starts
/// awaiting payment; pay it via POST api/orders/{orderId}/pay.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request.BuyerId = user.Identity?.Name;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var address = request.ShipToAddress is null
            ? null
            : new Address(request.ShipToAddress.Street ?? string.Empty,
                request.ShipToAddress.City ?? string.Empty,
                request.ShipToAddress.State ?? string.Empty,
                request.ShipToAddress.Country ?? string.Empty,
                request.ShipToAddress.ZipCode ?? string.Empty);

        var order = await orderPaymentService.CreateOrderAsync(request.BuyerId,
            request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList(),
            address);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = order.Currency
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
