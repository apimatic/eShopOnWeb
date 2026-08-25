using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public record SaveCardRequest(
    string CardNumber,
    int ExpiryMonth,
    int ExpiryYear,
    string Cvv,
    string CardholderName,
    string CountryCode,
    string? Street = null,
    string? City = null,
    string? State = null,
    string? PostalCode = null
);

public record SaveCardResponse(
    string PaymentMethodId,
    string? Last4,
    string? Brand,
    string? Expiry,
    string? CardType
);

public class SaveCardEndpoint : IEndpoint<IResult, SaveCardRequest, IPayPalPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SaveCardEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SaveCardRequest request, IPayPalPaymentService payPal) =>
            {
                return await HandleAsync(request, payPal);
            })
            .Produces<SaveCardResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SaveCardRequest request, IPayPalPaymentService payPal)
    {
        var httpCtx = _httpContextAccessor.HttpContext;
        var ct = httpCtx?.RequestAborted ?? default;
        var user = httpCtx?.User;
        var userId = user?.FindFirstValue(ClaimTypes.Email)
                  ?? user?.FindFirstValue("sub")
                  ?? user?.Identity?.Name;

        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var card = new CardPaymentDetails(
            request.CardNumber, request.ExpiryMonth, request.ExpiryYear,
            request.Cvv, request.CardholderName, request.CountryCode,
            request.Street, request.City, request.State, request.PostalCode);

        try
        {
            var result = await payPal.VaultCardAsync(userId, card, ct);
            return Results.Created($"api/payment-methods/{result.TokenId}",
                new SaveCardResponse(result.TokenId, result.Last4, result.Brand, result.Expiry, result.CardType));
        }
        catch (PayPalPaymentException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
        }
    }
}
