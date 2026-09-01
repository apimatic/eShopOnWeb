using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Authorize (hold) the order total via PayPal, using either one-off card details
/// or one of the shopper's saved cards. No money is taken until fulfilment.
/// </summary>
public class PayOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                PayPalCard? card = null;
                if (!string.IsNullOrWhiteSpace(request.CardNumber))
                {
                    if (request.ExpiryMonth is null || request.ExpiryYear is null)
                    {
                        throw new PaymentException("expiryMonth and expiryYear are required when paying with card details.");
                    }

                    card = new PayPalCard(
                        request.CardNumber.Replace(" ", string.Empty),
                        $"{request.ExpiryYear.Value:D4}-{request.ExpiryMonth.Value:D2}",
                        request.SecurityCode,
                        request.CardholderName,
                        request.BillingAddress is null ? null : new PayPalAddress(
                            request.BillingAddress.Street, null, request.BillingAddress.City,
                            request.BillingAddress.State, request.BillingAddress.ZipCode,
                            string.IsNullOrWhiteSpace(request.BillingAddress.Country) ? "US" : request.BillingAddress.Country));
                }

                var payment = await paymentService.PayOrderAsync(buyerId, orderId, card,
                    request.PaymentMethodId, cancellationToken);

                var response = new PayOrderResponse(request.CorrelationId())
                {
                    OrderId = orderId,
                    Payment = PaymentDto.FromPayment(payment)
                };
                return Results.Ok(response);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }
}

public class PayOrderRequest : BaseRequest
{
    /// <summary>Id of a saved card (from POST /api/payment-methods). Alternative to card details.</summary>
    public int? PaymentMethodId { get; set; }

    public string? CardNumber { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public AddressRequest? BillingAddress { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public PaymentDto? Payment { get; set; }
}
