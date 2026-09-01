using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper. The card is vaulted at PayPal; only safe
/// descriptors (brand, last digits, expiry) are stored and returned — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService paymentMethodService, CancellationToken ct) =>
            {
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, paymentMethodService, ct);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        return HandleAsync(request, paymentMethodService, CancellationToken.None);
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService, CancellationToken ct)
    {
        try
        {
            var savedCard = await paymentMethodService.SaveCardAsync(request.BuyerId, request.Card.ToModel(), ct);

            var response = new CreatePaymentMethodResponse(request.CorrelationId())
            {
                PaymentMethodId = savedCard.Id,
                Brand = savedCard.Brand,
                LastDigits = savedCard.LastDigits,
                Expiry = savedCard.Expiry
            };
            return Results.Created($"api/payment-methods/{savedCard.Id}", response);
        }
        catch (Exception ex) when (EndpointErrorMapper.TryMap(ex, out var error))
        {
            return error;
        }
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsDto Card { get; set; } = new();

    /// <summary>Set from the JWT by the endpoint; never accepted from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
}
