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
/// Authorizes (holds) the order total with PayPal, either with one-off card details or
/// with one of the shopper's saved cards. No money is captured until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var order = await orderPaymentService.PayAsync(request.BuyerId, request.OrderId,
            request.Card is null ? null : OrderMapper.ToModel(request.Card),
            request.SavedPaymentMethodId);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            PaymentStatus = order.PaymentStatus.ToString(),
            PayPalOrderId = order.PayPalOrderId,
            AuthorizationId = order.PayPalAuthorizationId,
            AuthorizedAmount = order.Total(),
            Currency = order.Currency,
            AuthorizationExpiresAt = order.AuthorizationExpiresAt
        };
        return Results.Ok(response);
    }
}
