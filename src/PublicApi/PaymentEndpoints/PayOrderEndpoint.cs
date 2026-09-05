using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Authorizes (holds) the order total: either with raw card details or with one of the
/// shopper's saved cards. The money is not taken until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(_httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal());
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        CardInput? card = null;
        if (request.PaymentMethodId == null)
        {
            card = PaymentEndpointHelpers.ToCardInput(request.Card, out var cardError);
            if (card == null)
            {
                return PaymentEndpointHelpers.FromError(cardError!);
            }
        }

        var result = await paymentService.PayOrderAsync(buyerId, request.OrderId, request.PaymentMethodId, card, default);
        if (!result.Succeeded)
        {
            return PaymentEndpointHelpers.FromError(result.Error!);
        }

        var payment = result.Payment!;
        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            PaymentId = payment.Id,
            Status = payment.Status == PaymentStatus.Authorized ? "Authorized" : payment.Status.ToString(),
            AuthorizationId = payment.PayPalAuthorizationId ?? string.Empty,
            AuthorizationStatus = payment.PayPalAuthorizationStatus ?? string.Empty,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            Amount = payment.Amount,
            Currency = payment.Currency
        };

        return Results.Ok(response);
    }
}



