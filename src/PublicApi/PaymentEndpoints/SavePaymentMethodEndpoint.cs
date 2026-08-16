using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class SavePaymentMethodRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public CardDto Card { get; set; } = new();
}

/// <summary>Identifies the saved card and describes it safely — never full card details.</summary>
public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Brand { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Saves a card for the signed-in shopper (vaulted at PayPal). Returns the paymentMethodId.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService service, CancellationToken ct) =>
            {
                request.BuyerId = user.BuyerId();
                return await HandleAsync(request, service, ct);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService service, CancellationToken ct)
    {
        if (request.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
        {
            throw new PaymentFlowException("Card details are required to save a payment method.", 400);
        }

        var saved = await service.SaveCardAsync(request.BuyerId, request.Card.ToCardPaymentDetails(), ct);
        var response = new SavePaymentMethodResponse
        {
            PaymentMethodId = saved.PaymentMethodId,
            Brand = saved.Brand,
            Last4 = saved.Last4,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName,
            CreatedAt = saved.CreatedAt
        };
        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", response);
    }
}
