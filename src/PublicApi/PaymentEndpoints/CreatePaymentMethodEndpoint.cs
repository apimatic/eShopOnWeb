using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper. Only safe card metadata is returned.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, FinixPaymentService payments, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(request, payments, user, ct);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreatePaymentMethodRequest request,
        FinixPaymentService payments,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());
        var buyerId = ClaimsHelper.GetBuyerId(user);

        if (request.Card is null)
            throw new FinixPaymentException(400, "CARD_REQUIRED",
                "Card details are required to save a payment method.");

        var saved = await payments.SaveCardAsync(buyerId, request.Card.ToInput(), ct);

        response.PaymentMethodId = saved.Id;
        response.Brand = saved.Brand;
        response.LastFour = saved.LastFour;
        response.ExpirationMonth = saved.ExpirationMonth;
        response.ExpirationYear = saved.ExpirationYear;
        response.CardholderName = saved.CardholderName;

        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
