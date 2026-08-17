using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;
using static Microsoft.eShopWeb.PublicApi.PaymentEndpoints.PaymentApiHelpers;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/pay — authorizes (holds) the order total. The request carries either raw
/// card details for a one-off payment, or the id of one of the shopper's saved cards. Idempotent: a
/// double-click never authorizes twice. Shopper-scoped: acts only on the caller's own order.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest>
{
    private readonly IPaymentService _paymentService;

    public PayOrderEndpoint(IPaymentService paymentService) => _paymentService = paymentService;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetUserName() ?? string.Empty;
                return await HandleAsync(request);
            })
            .Produces<PaymentView>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request)
    {
        var input = new PayInput(request.Card.ToCardDetails(), request.SavedPaymentMethodId);
        var result = await _paymentService.AuthorizeAsync(request.OrderId, request.BuyerId, input);
        return ToHttp(result, view => Results.Ok(view));
    }
}
