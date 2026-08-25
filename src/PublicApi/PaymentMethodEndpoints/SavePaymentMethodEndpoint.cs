using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPayPalService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, HttpContext ctx,
                   IRepository<SavedPaymentMethod> methodRepo,
                   IPayPalService paypal) =>
            {
                var username = ctx.User.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(username)) return Results.Unauthorized();

                if (string.IsNullOrWhiteSpace(request.CardNumber))
                    return Results.BadRequest("Card number is required.");

                var card = new CardDetails(
                    request.CardNumber,
                    request.ExpiryYear,
                    request.ExpiryMonth,
                    request.Cvv,
                    request.CardholderName,
                    request.BillingAddress?.Street,
                    request.BillingAddress?.City,
                    request.BillingAddress?.State,
                    request.BillingAddress?.Country,
                    request.BillingAddress?.ZipCode);

                VaultResult vaultResult;
                try
                {
                    vaultResult = await paypal.VaultCardAsync(username, card);
                }
                catch (PayPalException ex) when (ex.IsPayerActionRequired)
                {
                    return Results.Problem(
                        "PayPal requires 3DS payer action to save this card. " +
                        "Direct server-to-server card vaulting is not available for this card. " +
                        "Try a different card.",
                        statusCode: 422);
                }

                var method = new SavedPaymentMethod(
                    username,
                    vaultResult.VaultId,
                    vaultResult.PayPalCustomerId,
                    vaultResult.Last4,
                    vaultResult.Brand,
                    vaultResult.ExpiryYear,
                    vaultResult.ExpiryMonth);

                method = await methodRepo.AddAsync(method);

                return Results.Created($"api/payment-methods/{method.Id}", new SavePaymentMethodResponse
                {
                    PaymentMethodId = method.Id,
                    Last4 = vaultResult.Last4,
                    Brand = vaultResult.Brand,
                    ExpiryYear = vaultResult.ExpiryYear,
                    ExpiryMonth = vaultResult.ExpiryMonth
                });
            })
            .Produces<SavePaymentMethodResponse>(201)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPayPalService dependency)
        => throw new NotImplementedException();
}

public class SavePaymentMethodRequest
{
    public string CardNumber { get; set; } = "";
    public int ExpiryYear { get; set; }
    public int ExpiryMonth { get; set; }
    public string Cvv { get; set; } = "";
    public string CardholderName { get; set; } = "";
    public BillingAddressDto? BillingAddress { get; set; }
}

public class BillingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class SavePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string Last4 { get; set; } = "";
    public string Brand { get; set; } = "";
    public int ExpiryYear { get; set; }
    public int ExpiryMonth { get; set; }
}
