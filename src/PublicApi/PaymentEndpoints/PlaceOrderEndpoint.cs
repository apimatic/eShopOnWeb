using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — place an order from catalog items for the signed-in shopper. Reuses the
/// existing order/order-item model; amounts come from catalog prices. The order starts awaiting
/// payment. Returns the new order id as a top-level <c>orderId</c> field.
/// </summary>
public class PlaceOrderEndpoint : PaymentEndpointBase, IEndpoint<IResult, PlaceOrderRequest, IPaymentService>
{
    public PlaceOrderEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IPaymentService paymentService) =>
                await HandleAsync(request, paymentService))
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IPaymentService paymentService)
    {
        var items = (request.Items ?? new()).Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity)).ToList();
        var orderId = await paymentService.PlaceOrderAsync(BuyerId, items, request.ShipToAddress?.ToInput(), RequestAborted);
        return Results.Created($"api/orders/{orderId}", new { orderId });
    }
}
