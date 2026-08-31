using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Vaults a card at PayPal for the signed-in shopper. Only safe display
/// fields (brand, last digits, expiry) are ever stored or returned.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                return await HandleAsync(request, user, paymentService);
            })
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentService paymentService)
        => throw new NotImplementedException("Use the overload carrying the caller identity.");

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentService paymentService)
    {
        var buyerId = user.Identity!.Name!;
        var response = new SavePaymentMethodResponse(request.CorrelationId());

        var savedCard = await paymentService.SaveCardAsync(buyerId, request.Card.ToModel());

        response.PaymentMethodId = savedCard.Id;
        response.Card = SavedPaymentMethodDto.From(savedCard);
        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }
}

public class SavePaymentMethodRequest : BaseRequest
{
    public CardDetailsDto Card { get; set; } = new CardDetailsDto();
}

public class SavePaymentMethodResponse : BaseResponse
{
    public SavePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public int PaymentMethodId { get; set; }
    public SavedPaymentMethodDto Card { get; set; } = new SavedPaymentMethodDto();
}

public class SavedPaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static SavedPaymentMethodDto From(SavedPaymentMethod savedCard) => new SavedPaymentMethodDto
    {
        PaymentMethodId = savedCard.Id,
        Brand = savedCard.Brand,
        LastDigits = savedCard.LastDigits,
        Expiry = savedCard.Expiry,
        CardholderName = savedCard.CardholderName,
        CreatedAt = savedCard.CreatedAt
    };
}
