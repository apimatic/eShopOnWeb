using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Authorizes (holds) the order total. Carries either card details for a one-off payment or the id of
/// one of the caller's saved cards. Money is held, not taken. Idempotent: a double-click never
/// authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentOrderService service, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                request.OrderId = orderId;
                request.BuyerId = buyerId;
                return await HandleAsync(request, service, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IPaymentOrderService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentOrderService service, CancellationToken ct)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var order = await service.PayAsync(
            request.BuyerId,
            request.OrderId,
            request.Card?.ToCardDetails(),
            request.SavedPaymentMethodId,
            ct);

        response.OrderId = order.Id;
        response.Order = order.ToDto();
        return Results.Ok(response);
    }
}
