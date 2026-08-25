using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes (holds) the order's total with PayPal, using either fresh card details or a
/// previously saved payment method. Does not take the money - that happens at fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequestBody body, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                var request = new PayOrderRequest(user.Identity?.Name ?? string.Empty, orderId, body.Card, body.PaymentMethodId);
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var order = await orderPaymentService.AuthorizePaymentAsync(
            request.BuyerId,
            request.OrderId,
            request.Card?.ToPayPalCardDetails(),
            request.PaymentMethodId);

        response.OrderId = order.Id;
        response.Order = OrderMapper.ToDto(order);
        return Results.Ok(response);
    }
}
