using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total (places a hold; no money is taken yet).
/// Pays either with one-off card details or with one of the caller's saved cards.
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
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new PayOrderResponse(request.CorrelationId());

        if (request.Card == null && request.SavedCardId == null)
        {
            throw new PaymentConflictException("Provide either card details or a savedCardId to pay with.");
        }
        if (request.Card != null)
        {
            var validationError = request.Card.Validate();
            if (validationError != null)
            {
                throw new PaymentConflictException(validationError);
            }
        }

        var state = await orderPaymentService.PayOrderAsync(request.BuyerId, request.OrderId,
            request.Card?.ToCardDetails(), request.SavedCardId);
        if (state == null)
        {
            return Results.NotFound();
        }

        response.OrderId = state.Order.Id;
        response.Status = state.Order.Status.ToString();
        response.Payment = state.Payment == null ? null : PaymentStateDto.FromPayment(state.Payment);
        return Results.Ok(response);
    }
}
