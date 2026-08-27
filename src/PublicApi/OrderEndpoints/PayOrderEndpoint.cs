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
/// Authorizes the order total at PayPal (a hold on the money; nothing is taken yet),
/// either with raw card details or with one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        var payment = await paymentService.PayOrderAsync(
            CreateOrderEndpoint.GetBuyerId(user),
            request.OrderId,
            request.Card,
            request.SavedPaymentMethodId);

        response.OrderId = payment.OrderId;
        response.PaymentId = payment.PaymentId;
        response.Status = payment.Status;
        response.AuthorizationId = payment.AuthorizationId;
        response.AuthorizationStatus = payment.AuthorizationStatus;
        response.AuthorizedAmount = payment.AuthorizedAmount;
        response.Currency = payment.Currency;
        response.AuthorizationExpiresAt = payment.AuthorizationExpiresAt;

        return Results.Ok(response);
    }
}
