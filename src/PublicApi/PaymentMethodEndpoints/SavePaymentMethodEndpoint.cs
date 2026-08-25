using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public record SavePaymentMethodRequest(string CardNumber, string CardExpiry, string CardCvv, string? CardHolderName);
public record SavePaymentMethodResponse(string PaymentMethodId, string? LastFourDigits, string? CardBrand, string? Expiry);

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentService, IHttpContextAccessor>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, IPaymentService svc, IHttpContextAccessor ctx) =>
                await HandleAsync(request, svc, ctx))
            .Produces<SavePaymentMethodResponse>(201)
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentService svc, IHttpContextAccessor ctx)
    {
        var shopperId = ctx.HttpContext!.User.FindFirstValue(ClaimTypes.Email)
            ?? ctx.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.HttpContext.User.Identity?.Name
            ?? throw new UnauthorizedAccessException();

        var input = new SaveCardInput
        {
            CardNumber = request.CardNumber,
            CardExpiry = request.CardExpiry,
            CardCvv = request.CardCvv,
            CardHolderName = request.CardHolderName,
            BillingCountryCode = "US"
        };
        var card = await svc.SaveCardAsync(shopperId, input);

        return Results.Created($"/api/payment-methods/{card.PayPalPaymentTokenId}",
            new SavePaymentMethodResponse(card.PayPalPaymentTokenId, card.LastFourDigits, card.CardBrand, card.CardExpiry));
    }
}
