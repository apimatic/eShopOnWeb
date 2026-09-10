using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Card details to save. Full details are never stored or logged; they pass to PayPal's vault.</summary>
public class CreatePaymentMethodRequest
{
    public string? CardNumber { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? CardholderName { get; set; }
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

/// <summary>
/// POST /api/payment-methods — saves a card for the signed-in shopper via PayPal's vault. Shopper-scoped.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedCardService savedCardService, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var input = new SaveCardInput
                {
                    CardNumber = request.CardNumber,
                    Expiry = request.Expiry,
                    SecurityCode = request.SecurityCode,
                    CardholderName = request.CardholderName
                };

                var saved = await savedCardService.SaveCardAsync(buyerId, input, ct);
                return Results.Created($"api/payment-methods/{saved.Id}", new CreatePaymentMethodResponse
                {
                    PaymentMethodId = saved.Id,
                    Brand = saved.Brand,
                    LastDigits = saved.LastDigits,
                    Expiry = saved.Expiry,
                    CardholderName = saved.CardholderName
                });
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
