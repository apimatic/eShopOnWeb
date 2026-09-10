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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — a signed-in shopper places an order from catalog items. Prices come from the
/// catalog; the order starts awaiting payment. The caller's identity comes from the token.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, paymentService);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IPaymentService paymentService)
    {
        var s = request.ShipTo;
        var address = new Address(
            s?.Street ?? "N/A",
            s?.City ?? "N/A",
            s?.State ?? "N/A",
            s?.Country ?? "N/A",
            s?.ZipCode ?? "00000");

        var lines = request.Items.Select(i => new OrderLineItem(i.CatalogItemId, i.Quantity));
        var order = await paymentService.PlaceOrderAsync(request.BuyerId, lines, address);

        var response = new PlaceOrderResponse { OrderId = order.Id, Order = OrderSummaryDto.From(order) };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
