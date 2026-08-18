using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreatePaymentMethodRequest
{
    public CardRequestDto Card { get; set; } = new();

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string LastFourDigits { get; set; } = string.Empty;
    public string? Expiry { get; set; }
}

/// <summary>Save (vault) a card for the signed-in shopper. Returns a safe descriptor — never full details.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, IPaymentService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.BuyerId = PaymentMappers.BuyerId(user);
                return await HandleAsync(request, service, ct);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentService service)
        => HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentService service, CancellationToken ct)
    {
        if (request.Card is null)
        {
            throw new PaymentValidationException("Card details are required to save a payment method.");
        }

        var card = PaymentMappers.ToCardDetails(request.Card);
        var savedCard = await service.SaveCardAsync(request.BuyerId, card, ct);

        var response = new CreatePaymentMethodResponse
        {
            PaymentMethodId = savedCard.Id,
            CardBrand = savedCard.CardBrand,
            LastFourDigits = savedCard.LastFourDigits,
            Expiry = savedCard.Expiry
        };

        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }
}
