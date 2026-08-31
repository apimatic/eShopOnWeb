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
/// Authorizes the order total (a hold, not a charge) using either raw card
/// details or one of the shopper's saved cards.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest>
{
    private readonly IPaymentService _paymentService;

    public PayOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request)
    {
        if (request.PaymentMethodId is null && request.Card is null)
        {
            return Results.BadRequest("Provide either 'paymentMethodId' (a saved card) or 'card' details.");
        }

        var payment = await _paymentService.PayOrderAsync(
            request.BuyerId, request.OrderId,
            request.Card?.ToCardDetails(), request.PaymentMethodId);

        if (payment is null)
        {
            return Results.NotFound();
        }

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Status = "PaymentAuthorized",
            Payment = PaymentDto.FromPayment(payment)
        };
        return Results.Ok(response);
    }
}
