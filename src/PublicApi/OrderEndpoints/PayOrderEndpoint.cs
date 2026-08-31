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
/// Authorizes (holds) the order total — with raw card details or one of the shopper's saved cards.
/// No money moves until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService, CancellationToken ct) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, user, paymentService, ct);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService, CancellationToken ct)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var hasCard = request.Card != null;
        var hasSavedMethod = request.SavedPaymentMethodId.HasValue;
        if (hasCard == hasSavedMethod)
        {
            return Results.BadRequest(new { message = "Supply exactly one payment source: card or savedPaymentMethodId." });
        }
        if (hasCard && (string.IsNullOrWhiteSpace(request.Card!.Number) || string.IsNullOrWhiteSpace(request.Card.Expiry)))
        {
            return Results.BadRequest(new { message = "Card number and expiry (YYYY-MM) are required." });
        }

        var payment = await paymentService.PayAsync(
            buyerId,
            request.OrderId,
            MapCard(request.Card),
            request.SavedPaymentMethodId,
            ct);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = payment.OrderId,
            PaymentId = payment.Id,
            OrderStatus = "PaymentAuthorized",
            PaymentStatus = payment.Status.ToString(),
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            Amount = payment.Amount,
            Currency = payment.Currency
        };

        return Results.Ok(response);
    }

    internal static CardDetails? MapCard(CardDetailsDto? dto)
        => dto == null
            ? null
            : new CardDetails(
                dto.Number,
                dto.Expiry,
                dto.SecurityCode,
                dto.Name,
                dto.BillingAddress == null
                    ? null
                    : new BillingAddressDetails(
                        dto.BillingAddress.AddressLine1,
                        dto.BillingAddress.AddressLine2,
                        dto.BillingAddress.City,
                        dto.BillingAddress.State,
                        dto.BillingAddress.PostalCode,
                        dto.BillingAddress.CountryCode));
}
