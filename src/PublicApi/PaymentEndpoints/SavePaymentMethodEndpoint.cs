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
    public CardInput Card { get; set; } = new();
}

public record SavePaymentMethodResponse(
    int PaymentMethodId,
    string? Brand,
    string? Last4,
    string? Expiry,
    string? CardholderName);

/// <summary>POST /api/payment-methods — saves (vaults) a card for the signed-in shopper.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodRequest request,
                IOrderPaymentService service,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            await PaymentApiSupport.ExecuteAsync(async () =>
            {
                var buyerId = PaymentApiSupport.RequireBuyerId(user);
                if (request.Card is null)
                    throw new PaymentValidationException("Card details are required.");

                var method = await service.SavePaymentMethodAsync(buyerId, PaymentApiSupport.ToCardDetails(request.Card), ct);
                var response = new SavePaymentMethodResponse(
                    PaymentMethodId: method.Id,
                    Brand: method.CardBrand,
                    Last4: method.CardLast4,
                    Expiry: method.CardExpiry,
                    CardholderName: method.CardholderName);
                return Results.Created($"api/payment-methods/{method.Id}", response);
            }))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("Payments");
    }
}
