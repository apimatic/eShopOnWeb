using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves (vaults) a card for the signed-in shopper. The response identifies the saved card
/// and describes it safely (brand, last digits, expiry) — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;

    public SavePaymentMethodEndpoint(
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway)
    {
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, ct);
            })
            .Produces<SavePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request, ClaimsPrincipal user) =>
        HandleAsync(request, user, CancellationToken.None);

    private async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ClaimsPrincipal user,
        CancellationToken ct)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var card = new CardPaymentDetails(
            Number: request.Number,
            Expiry: request.Expiry,
            SecurityCode: request.SecurityCode,
            CardholderName: request.CardholderName,
            BillingAddress: request.BillingAddress is null
                ? null
                : new GatewayAddress(
                    CountryCode: request.BillingAddress.CountryCode,
                    AddressLine1: request.BillingAddress.AddressLine1,
                    AddressLine2: request.BillingAddress.AddressLine2,
                    City: request.BillingAddress.City,
                    State: request.BillingAddress.State,
                    PostalCode: request.BillingAddress.PostalCode));

        var savedCard = await _paymentGateway.SaveCardAsync(
            merchantCustomerId: buyerId,
            card: card,
            idempotencyKey: $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            ct: ct);

        var entity = new SavedPaymentMethod(buyerId, savedCard.VaultId, savedCard.PayPalCustomerId,
            savedCard.Brand, savedCard.LastDigits, savedCard.Expiry);
        entity = await _paymentMethodRepository.AddAsync(entity, ct);

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = entity.Id,
            Brand = entity.Brand,
            LastDigits = entity.LastDigits,
            Expiry = entity.Expiry
        };
        return Results.Created($"api/payment-methods/{entity.Id}", response);
    }
}
