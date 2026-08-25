using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class GetPaymentMethodsEndpoint : IEndpoint<IResult, string>
{
    private readonly IPayPalPaymentService _payPal;

    public GetPaymentMethodsEndpoint(IPayPalPaymentService payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx, CancellationToken ct) =>
            {
                var buyerId = ctx.User.Identity?.Name ?? string.Empty;
                return await HandleAsync(buyerId);
            })
            .Produces<GetPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId)
    {
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        try
        {
            var cards = await _payPal.ListSavedCardsAsync(merchantCustomerId: buyerId);

            var dtos = cards.Select(c => new SavedCardDto
            {
                PaymentMethodId = c.PaymentTokenId,
                LastFourDigits = c.LastFourDigits,
                CardBrand = c.CardBrand,
                Expiry = c.Expiry
            }).ToList();

            return Results.Ok(new GetPaymentMethodsResponse { PaymentMethods = dtos });
        }
        catch (PayPalOperationException ex)
        {
            return Results.Problem(
                title: "Failed to list payment methods",
                detail: ex.Message,
                statusCode: (int)ex.StatusCode);
        }
    }
}

public class GetPaymentMethodsResponse
{
    public System.Collections.Generic.List<SavedCardDto> PaymentMethods { get; set; } = new();
}

public class SavedCardDto
{
    public string PaymentMethodId { get; set; } = string.Empty;
    public string? LastFourDigits { get; set; }
    public string? CardBrand { get; set; }
    public string? Expiry { get; set; }
}
