using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Serialization;
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

public class CreatePaymentMethodRequest : BaseRequest
{
    [Required]
    public CardDetailsDto Card { get; set; } = new();

    /// <summary>Populated from the JWT; never read from the request body.</summary>
    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) {}
    public CreatePaymentMethodResponse() {}

    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastFourDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

/// <summary>
/// Saves a card for the signed-in shopper by vaulting it with PayPal. Only safe display
/// data (brand, last four digits, expiry) is returned and stored.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedPaymentMethodService paymentMethodService) =>
            {
                request.BuyerId = user.Identity?.Name;
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService paymentMethodService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var saved = await paymentMethodService.SaveCardAsync(request.BuyerId, OrderMapper.ToModel(request.Card));

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand,
            LastFourDigits = saved.LastFourDigits,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
