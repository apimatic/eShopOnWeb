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
/// Authorizes (holds) the order total on the shopper's card — one-off card details
/// or one of the shopper's saved cards. No money moves until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, orderPaymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService)
    {
        var buyerId = user.GetCallerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        if (request.Card is not null && !request.Card.IsValid())
        {
            return Results.BadRequest(new { Message = "Card number, expiry (YYYY-MM), security code and cardholder name are required." });
        }

        var payment = await orderPaymentService.PayAsync(buyerId, request.OrderId, new PayOrderCommand
        {
            Card = request.Card?.ToGatewayCardDetails(),
            SavedPaymentMethodId = request.PaymentMethodId
        });

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Status = "PaymentAuthorized",
            Payment = PaymentDto.FromPayment(payment)
        };
        return Results.Ok(response);
    }
}
