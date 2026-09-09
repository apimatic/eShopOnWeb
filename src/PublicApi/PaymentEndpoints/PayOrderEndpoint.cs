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
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total. The request carries either
/// card details for a one-off payment or the id of one of the shopper's saved cards. Idempotent in
/// effect: a double-click never authorizes twice. Shopper-scoped to the caller's own order.
/// </summary>
public class PayOrderEndpoint : PaymentEndpointBase, IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var command = new PayCommand(request.Card?.ToGatewayCard(), request.SavedPaymentMethodId);
        var view = await paymentService.PayAsync(BuyerId, request.OrderId, command, RequestAborted);
        return Results.Ok(view);
    }
}
