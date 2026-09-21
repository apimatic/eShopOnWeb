using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorize (hold) the order total against a one-off card or a
/// saved card. Does not capture. Idempotent: a double-click never authorizes twice.
/// </summary>
public class PayOrderEndpoint : PaymentEndpointBase, IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService service) =>
            {
                request.OrderId = orderId; // authoritative id comes from the route, not the body
                return await HandleAsync(request, service);
            })
            .Produces<OrderPaymentView>()
            .WithTags("PaymentOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService service)
    {
        CardDetails? card = request.Card is null ? null : PaymentMappings.ToCardDetails(request.Card);
        var view = await service.PayAsync(BuyerId, request.OrderId, card, request.SavedPaymentMethodId, RequestAborted);
        return Results.Ok(view);
    }
}
