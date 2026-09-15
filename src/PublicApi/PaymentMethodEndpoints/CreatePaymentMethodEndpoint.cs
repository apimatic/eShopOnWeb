using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IOrderCheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, ClaimsPrincipal user, IOrderCheckoutService checkout) =>
                await HandleAsync(request, user, checkout))
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IOrderCheckoutService checkout)
        => Task.FromResult(Results.BadRequest());

    private static async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user, IOrderCheckoutService checkout)
    {
        var buyerId = CheckoutHttp.BuyerId(user);
        var card = request.Card ?? throw new ApplicationCore.Exceptions.PaymentException(400, "Card details are required.");
        var saved = await checkout.SaveCardAsync(buyerId, new CardPaymentInput
        {
            Number = card.Number ?? string.Empty,
            Expiry = card.Expiry ?? string.Empty,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress is null
                ? null
                : new CardBillingAddress
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                }
        });

        var response = CheckoutHttp.ToPaymentMethodResponse(saved);
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

public class CreatePaymentMethodRequest
{
    public PayCardRequest? Card { get; set; }
}
