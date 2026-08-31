using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorizes the order total: puts a hold on the money without taking it.
/// Pay either with raw card details or with one of the caller's saved cards.
/// Repeating the call returns the existing hold instead of charging twice.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, int, ClaimsPrincipal>
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
                return await HandleAsync(request, orderId, user);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, int orderId, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Card is not null)
        {
            ValidateCard(request.Card);
        }

        var payment = await _paymentService.PayAsync(buyerId, orderId,
            request.Card?.ToPayPalCard(), request.PaymentMethodId);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = payment.OrderId,
            PaymentId = payment.Id,
            Status = payment.Status.ToString(),
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizedAmount = payment.Amount,
            Currency = payment.Currency,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt
        };
        return Results.Ok(response);
    }

    private static void ValidateCard(PaymentMethodEndpoints.CardDetailsDto card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || card.Number.Length is < 13 or > 19)
        {
            throw new PaymentDomainException("Card number must be 13-19 digits.");
        }
        if (string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentDomainException("Card expiry is required (format: YYYY-MM).");
        }
    }
}
