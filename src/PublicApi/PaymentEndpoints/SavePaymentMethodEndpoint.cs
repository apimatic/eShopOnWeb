using System;
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
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper (vaulted at PayPal). The response identifies the
/// saved card and describes it safely — brand and last digits — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Save a card for later reuse", Tags = new[] { "PaymentMethods" })]
        async (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
                await HandleAsync(request, user, service, ct))
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct)
    {
        var buyerId = PaymentProblem.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        if (request?.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
            return Results.BadRequest(new { message = "Card details are required to save a payment method." });

        try
        {
            var saved = await service.SaveCardAsync(buyerId, request.Card.ToDomain(), ct);
            var response = new PaymentMethodResponse(
                saved.Id, saved.CardBrand, saved.LastDigits, saved.Expiry, saved.CardholderName, saved.CreatedAt);
            return Results.Created($"api/payment-methods/{saved.Id}", response);
        }
        catch (Exception ex)
        {
            return PaymentProblem.ToResult(ex);
        }
    }
}
