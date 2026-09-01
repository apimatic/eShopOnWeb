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
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Save a card for the signed-in shopper. The card is vaulted with PayPal; only safe
/// display data (brand, last digits, expiry) is ever stored or returned.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user,
                ISavedPaymentMethodService savedPaymentMethodService, CancellationToken cancellationToken) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (string.IsNullOrWhiteSpace(request.CardNumber)
                    || request.ExpiryMonth is null || request.ExpiryYear is null)
                {
                    throw new PaymentException("cardNumber, expiryMonth and expiryYear are required to save a card.");
                }

                var card = new PayPalCard(
                    request.CardNumber.Replace(" ", string.Empty),
                    $"{request.ExpiryYear.Value:D4}-{request.ExpiryMonth.Value:D2}",
                    request.SecurityCode,
                    request.CardholderName,
                    request.BillingAddress is null ? null : new PayPalAddress(
                        request.BillingAddress.Street, null, request.BillingAddress.City,
                        request.BillingAddress.State, request.BillingAddress.ZipCode,
                        string.IsNullOrWhiteSpace(request.BillingAddress.Country) ? "US" : request.BillingAddress.Country));

                var saved = await savedPaymentMethodService.SaveCardAsync(buyerId, card, cancellationToken);

                var response = new CreatePaymentMethodResponse(request.CorrelationId())
                {
                    PaymentMethodId = saved.Id,
                    PaymentMethod = PaymentMethodDto.FromSavedPaymentMethod(saved)
                };
                return Results.Created($"api/payment-methods/{saved.Id}", response);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string? CardNumber { get; set; }
    public int? ExpiryMonth { get; set; }
    public int? ExpiryYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
    public AddressRequest? BillingAddress { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public PaymentMethodDto? PaymentMethod { get; set; }
}
