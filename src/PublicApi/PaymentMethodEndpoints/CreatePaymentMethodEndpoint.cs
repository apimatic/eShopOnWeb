using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card with PayPal's vault for the signed-in shopper. The response identifies
/// the saved card and describes it safely (brand + last digits) - never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService, HttpContext http, CancellationToken ct) =>
            {
                return await HandleAsync(request, paymentMethodService, http, ct);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService) =>
        HandleAsync(request, paymentMethodService, httpContext: null, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService, HttpContext? httpContext, CancellationToken ct)
    {
        var buyerId = httpContext?.User?.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (request.Card == null || string.IsNullOrWhiteSpace(request.Card.Number) ||
            string.IsNullOrWhiteSpace(request.Card.Expiry) || string.IsNullOrWhiteSpace(request.Card.Cvc))
        {
            return Results.BadRequest(new { message = "Card number, expiry and cvc are required." });
        }

        var card = new GatewayCard(
            request.Card.Number,
            request.Card.Expiry,
            request.Card.Cvc,
            request.Card.Name,
            request.Card.BillingAddress == null ? null : new GatewayAddress(
                request.Card.BillingAddress.AddressLine1,
                request.Card.BillingAddress.AddressLine2,
                request.Card.BillingAddress.AdminArea1,
                request.Card.BillingAddress.AdminArea2,
                request.Card.BillingAddress.PostalCode,
                request.Card.BillingAddress.CountryCode));

        var paymentMethod = await paymentMethodService.SaveCardAsync(buyerId, card, ct);

        return Results.Created($"api/payment-methods/{paymentMethod.Id}", new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = paymentMethod.Id,
            Brand = paymentMethod.Brand,
            LastDigits = paymentMethod.LastDigits,
            Expiry = paymentMethod.Expiry
        });
    }
}
