using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved
/// card and shows only safe display data - never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;

    public CreatePaymentMethodEndpoint(IRepository<SavedPaymentMethod> paymentMethodRepository, IPaymentGateway paymentGateway)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());

        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (request.Card is null)
        {
            return Results.BadRequest("Card details are required.");
        }

        GatewayVaultedCard vaultedCard;
        try
        {
            vaultedCard = await _paymentGateway.VaultCardAsync(
                PayOrderEndpoint.ToCardDetails(request.Card),
                $"eshop-vault-{Guid.NewGuid():N}");
        }
        catch (PaymentGatewayException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message, gatewayError = ex.GatewayErrorName });
        }

        var savedCard = new SavedPaymentMethod(buyerId, vaultedCard.VaultTokenId,
            vaultedCard.Brand, vaultedCard.LastDigits, vaultedCard.Expiry, vaultedCard.CardholderName);
        savedCard = await _paymentMethodRepository.AddAsync(savedCard);

        response.PaymentMethodId = savedCard.Id;
        response.Brand = savedCard.Brand;
        response.LastDigits = savedCard.LastDigits;
        response.Expiry = savedCard.Expiry;
        response.CardholderName = savedCard.CardholderName;
        return Results.Created($"api/payment-methods/{savedCard.Id}", response);
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardRequest? Card { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}
