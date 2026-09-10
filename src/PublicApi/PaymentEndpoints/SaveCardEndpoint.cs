using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class SaveCardRequest : BaseRequest
{
    public string Number { get; set; } = string.Empty;
    /// <summary>Expiry in YYYY-MM format.</summary>
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public BillingAddressDto? BillingAddress { get; set; }
}

public class SaveCardResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

/// <summary>
/// POST /api/payment-methods — vaults a card for the signed-in shopper. Returns the safe card
/// description (never full card details) and the new paymentMethodId as a top-level field.
/// </summary>
public class SaveCardEndpoint : IEndpoint<IResult, SaveCardRequest, ClaimsPrincipal, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SaveCardRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
                await HandleAsync(request, user, paymentService))
            .Produces<SaveCardResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SaveCardRequest request, ClaimsPrincipal user,
        IPaymentService paymentService)
    {
        var buyerId = CallerId.BuyerId(user);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var card = new GatewayCardDetails(
            Number: request.Number,
            Expiry: request.Expiry,
            SecurityCode: request.SecurityCode,
            Name: request.Name,
            BillingAddress: request.BillingAddress is null ? null : new GatewayBillingAddress(
                request.BillingAddress.AddressLine1,
                request.BillingAddress.AddressLine2,
                request.BillingAddress.AdminArea2,
                request.BillingAddress.AdminArea1,
                request.BillingAddress.PostalCode,
                request.BillingAddress.CountryCode));

        var saved = await paymentService.SaveCardAsync(buyerId, card);

        var response = new SaveCardResponse
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand,
            LastDigits = saved.LastDigits,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName
        };
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
