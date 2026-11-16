using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Authorizes the order total. Money is held, not captured.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, FinixPaymentService payments, System.Security.Claims.ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, request, payments, user, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        int orderId,
        PayOrderRequest request,
        FinixPaymentService payments,
        System.Security.Claims.ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var response = new PayOrderResponse(request.CorrelationId());
        var buyerId = ClaimsHelper.GetBuyerId(user);

        var order = await payments.PayAsync(
            buyerId,
            orderId,
            request.PaymentMethodId,
            request.Card?.ToInput(),
            ct);

        response.OrderId = order.Id;
        response.PaymentStatus = order.PaymentStatus.ToString();
        response.AuthorizationId = order.FinixAuthorizationId;
        response.AuthorizationState = order.AuthorizationState;
        response.AuthorizedAmountCents = order.AuthorizedAmountCents;
        response.Currency = order.Currency;
        response.PaymentMethodId = request.PaymentMethodId;

        return Results.Ok(response);
    }
}
