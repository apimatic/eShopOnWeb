using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response describes the card safely (brand +
/// last four + expiry) and returns the <c>paymentMethodId</c>. The full card number is never stored.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, HttpContext context) => await HandleAsync(request, context))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, HttpContext context)
    {
        var response = new SavePaymentMethodResponse(request.CorrelationId());
        var service = context.RequestServices.GetRequiredService<IPaymentMethodService>();

        var card = (request.Card ?? new CardModel()).ToCardDetails();
        var saved = await service.SaveCardAsync(context.User.BuyerId(), card);

        response.PaymentMethodId = saved.Id;
        response.Brand = saved.Brand;
        response.LastFourDigits = saved.LastFourDigits;
        response.Expiry = saved.Expiry;
        response.CardholderName = saved.CardholderName;

        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

public class SavePaymentMethodRequest : BaseRequest
{
    public CardModel? Card { get; set; }
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public SavePaymentMethodResponse() { }

    /// <summary>Top-level identifier of the saved card.</summary>
    public int PaymentMethodId { get; set; }

    public string Brand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
}
