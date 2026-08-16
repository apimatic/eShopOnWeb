using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// Returns the created order's id as a top-level <c>orderId</c>.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IPaymentOrderService service, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                request.BuyerId = buyerId;
                return await HandleAsync(request, service, ct);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentOrderService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentOrderService service, CancellationToken ct)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        var lines = request.Items
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var orderId = await service.PlaceOrderAsync(request.BuyerId, lines, request.ShipToAddress?.ToAddress(), ct);

        var orders = await service.GetOrdersForBuyerAsync(request.BuyerId, ct);
        response.OrderId = orderId;
        response.Order = orders.FirstOrDefault(o => o.Id == orderId)?.ToDto();

        return Results.Created($"api/orders/{orderId}", response);
    }
}
