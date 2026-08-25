using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves a card for the signed-in shopper via PayPal's vault. The raw card number is sent straight to PayPal and never stored by this app.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, BuyerContext<IPaymentMethodService>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService paymentMethodService) =>
            {
                var context = new BuyerContext<IPaymentMethodService>(user.Identity!.Name!, paymentMethodService);
                return await HandleAsync(request, context);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, BuyerContext<IPaymentMethodService> context)
    {
        if (string.IsNullOrEmpty(request.CardNumber) || string.IsNullOrEmpty(request.CardExpiry) ||
            string.IsNullOrEmpty(request.CardSecurityCode) || string.IsNullOrEmpty(request.BillingAddressCountryCode))
        {
            return Results.BadRequest("CardNumber, CardExpiry, CardSecurityCode and BillingAddressCountryCode are required.");
        }

        var billingAddress = new PaymentAddress(
            request.BillingAddressLine1, request.BillingAddressCity, request.BillingAddressState,
            request.BillingAddressPostalCode, request.BillingAddressCountryCode);
        var card = new CardDetails(request.CardNumber, request.CardExpiry, request.CardSecurityCode, request.CardholderName, billingAddress);

        var paymentMethod = await context.Service.SaveCardAsync(context.BuyerId, card, default);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = paymentMethod.Id,
            PaymentMethod = PaymentMethodDto.FromPaymentMethod(paymentMethod)
        };
        return Results.Created("api/payment-methods", response);
    }
}
