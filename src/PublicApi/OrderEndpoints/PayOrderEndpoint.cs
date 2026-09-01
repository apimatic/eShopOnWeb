using System.Security.Claims;
using System.Threading;
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
/// Authorizes the order total — a hold on the money, not a capture — either with raw card
/// details or with one of the shopper's saved cards. Repeating the call on an already-paid
/// order returns the existing hold; it never authorizes twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ClaimsPrincipal>
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
                return await HandleAsync(request, user);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var hasCard = request.Card is not null;
        var hasSavedCard = request.PaymentMethodId is not null;
        if (hasCard == hasSavedCard)
        {
            return Results.BadRequest(new { message = "Provide exactly one of 'card' or 'paymentMethodId'." });
        }

        Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate.Payment payment;
        if (hasCard)
        {
            var validationError = request.Card!.Validate();
            if (validationError is not null)
            {
                return Results.BadRequest(new { message = validationError });
            }
            payment = await _paymentService.PayWithCardAsync(buyerId, request.OrderId, request.Card!.ToModel(), CancellationToken.None);
        }
        else
        {
            payment = await _paymentService.PayWithSavedCardAsync(buyerId, request.OrderId, request.PaymentMethodId!.Value, CancellationToken.None);
        }

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Status = "PaymentAuthorized",
            Payment = PaymentDto.FromModel(payment)
        };
        return Results.Ok(response);
    }
}
