using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IRepository<PaymentMethod>>
{
    private readonly IPayPalService _payPal;

    public CreatePaymentMethodEndpoint(IPayPalService payPal) => _payPal = payPal;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request,
                   IRepository<PaymentMethod> methodRepository,
                   HttpContext ctx) =>
            {
                var buyer = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyer))
                    return Results.Unauthorized();

                if (request.Card == null)
                    return Results.BadRequest(new { error = "card details are required." });

                // Look up existing PayPal customer ID for this buyer (from any existing payment method)
                var existingSpec = new PaymentMethodsByBuyerIdSpec(buyer);
                var existingMethods = await methodRepository.ListAsync(existingSpec);
                string? existingPayPalCustomerId = null;
                foreach (var m in existingMethods)
                {
                    if (!string.IsNullOrEmpty(m.PayPalCustomerId))
                    {
                        existingPayPalCustomerId = m.PayPalCustomerId;
                        break;
                    }
                }

                var card = new CardPaymentDetails(
                    Number: request.Card.Number,
                    Expiry: request.Card.Expiry,
                    SecurityCode: request.Card.SecurityCode,
                    CardholderName: request.Card.CardholderName,
                    AddressLine1: request.Card.BillingAddress?.AddressLine1 ?? string.Empty,
                    City: request.Card.BillingAddress?.City ?? string.Empty,
                    State: request.Card.BillingAddress?.State ?? string.Empty,
                    CountryCode: request.Card.BillingAddress?.CountryCode ?? "US",
                    PostalCode: request.Card.BillingAddress?.PostalCode ?? string.Empty);

                VaultResult vaultResult;
                try
                {
                    vaultResult = await _payPal.VaultCardAsync(buyer, existingPayPalCustomerId, card, ctx.RequestAborted);
                }
                catch (PayPalException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }

                var method = new PaymentMethod(
                    buyerId: buyer,
                    payPalTokenId: vaultResult.TokenId,
                    payPalCustomerId: vaultResult.CustomerId,
                    cardLastFour: vaultResult.LastFour,
                    cardBrand: vaultResult.Brand,
                    cardExpiry: vaultResult.Expiry);

                method = await methodRepository.AddAsync(method);

                var response = new CreatePaymentMethodResponse(request.CorrelationId())
                {
                    PaymentMethodId = method.Id,
                    LastFour = vaultResult.LastFour,
                    Brand = vaultResult.Brand,
                    Expiry = vaultResult.Expiry
                };
                return Results.Created($"api/payment-methods/{method.Id}", response);
            })
            .Produces<CreatePaymentMethodResponse>(201)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IRepository<PaymentMethod> repository)
        => Task.FromResult(Results.StatusCode(501) as IResult);
}
