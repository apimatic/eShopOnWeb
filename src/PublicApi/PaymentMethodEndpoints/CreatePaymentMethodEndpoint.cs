using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper so a later order can be paid without re-entering it.
/// The raw card number is sent to PayPal once to vault it and is never stored by this app.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ClaimsPrincipal, IRepository<PaymentMethod>>
{
    private readonly IPaymentProvider _paymentProvider;

    public CreatePaymentMethodEndpoint(IPaymentProvider paymentProvider)
    {
        _paymentProvider = paymentProvider;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, IRepository<PaymentMethod> paymentMethodRepository) =>
            {
                return await HandleAsync(request, user, paymentMethodRepository);
            })
            .Produces<CreatePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user, IRepository<PaymentMethod> paymentMethodRepository)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var card = new CardDetails(
            request.Card.Name,
            request.Card.Number,
            request.Card.Expiry,
            request.Card.SecurityCode,
            request.Card.AddressLine1,
            request.Card.City,
            request.Card.PostalCode,
            request.Card.CountryCode);

        var saved = await _paymentProvider.SaveCardAsync(card, $"vault-{buyerId}-{Guid.NewGuid()}", CancellationToken.None);

        var paymentMethod = new PaymentMethod(buyerId, saved.VaultId, saved.CardBrand, saved.LastDigits, saved.Expiry, DateTimeOffset.UtcNow);
        paymentMethod = await paymentMethodRepository.AddAsync(paymentMethod);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = paymentMethod.Id,
            PaymentMethod = PaymentMethodDto.FromPaymentMethod(paymentMethod)
        };

        return Results.Created($"api/payment-methods/{paymentMethod.Id}", response);
    }
}
