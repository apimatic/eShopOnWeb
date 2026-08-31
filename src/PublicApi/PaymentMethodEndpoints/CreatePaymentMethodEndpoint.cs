using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card with PayPal for the signed-in shopper. Only safe
/// display data (brand, last digits, expiry) is stored by this application.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest>
{
    private readonly IPayPalClient _payPalClient;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly ICurrentUser _currentUser;

    public CreatePaymentMethodEndpoint(IPayPalClient payPalClient,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        ICurrentUser currentUser)
    {
        _payPalClient = payPalClient;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _currentUser = currentUser;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request)
    {
        var response = new CreatePaymentMethodResponse(request.CorrelationId());

        if (request.Card is null)
        {
            throw new ArgumentException("Card details are required.", nameof(request));
        }

        var buyerId = _currentUser.BuyerId;
        var vaultedCard = await _payPalClient.VaultCardAsync(
            PayOrderEndpoint.MapCard(request.Card)!,
            merchantCustomerId: buyerId,
            idempotencyKey: $"eshop-vault-{Guid.NewGuid():N}");

        var savedMethod = new SavedPaymentMethod(
            buyerId,
            vaultedCard.VaultTokenId,
            vaultedCard.Brand,
            vaultedCard.LastDigits,
            vaultedCard.Expiry,
            vaultedCard.CardholderName);

        await _savedPaymentMethodRepository.AddAsync(savedMethod);

        response.PaymentMethodId = savedMethod.Id;
        response.Brand = savedMethod.Brand;
        response.LastDigits = savedMethod.LastDigits;
        response.Expiry = savedMethod.Expiry;
        response.CardholderName = savedMethod.CardholderName;
        return Results.Created($"api/payment-methods/{savedMethod.Id}", response);
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardRequest? Card { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) {}
    public CreatePaymentMethodResponse() {}

    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}
