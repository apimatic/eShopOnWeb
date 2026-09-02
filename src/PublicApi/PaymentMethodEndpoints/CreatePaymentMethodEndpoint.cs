using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Vaults a card at PayPal for the signed-in shopper. Only safe display data
/// (brand, last four digits, expiry) is returned and stored.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ClaimsPrincipal>
{
    private readonly ISavedPaymentMethodService _savedPaymentMethodService;

    public CreatePaymentMethodEndpoint(ISavedPaymentMethodService savedPaymentMethodService)
    {
        _savedPaymentMethodService = savedPaymentMethodService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Number) || string.IsNullOrWhiteSpace(request.SecurityCode))
        {
            return Results.BadRequest("Card number and security code are required.");
        }

        var card = new CardDetails
        {
            Number = request.Number,
            ExpiryMonth = request.ExpiryMonth,
            ExpiryYear = request.ExpiryYear,
            SecurityCode = request.SecurityCode,
            CardholderName = request.CardholderName ?? string.Empty,
            BillingAddressLine1 = request.BillingAddressLine1,
            BillingAddressLine2 = request.BillingAddressLine2,
            BillingCity = request.BillingCity,
            BillingState = request.BillingState,
            BillingPostalCode = request.BillingPostalCode,
            BillingCountryCode = request.BillingCountryCode
        };

        try
        {
            var saved = await _savedPaymentMethodService.SaveCardAsync(buyerId, card);
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
        catch (PaymentVerificationRequiredException ex)
        {
            return Results.UnprocessableEntity(new { message = ex.Message });
        }
        catch (PaymentDeclinedException ex)
        {
            return Results.UnprocessableEntity(new { message = ex.Message });
        }
    }
}
