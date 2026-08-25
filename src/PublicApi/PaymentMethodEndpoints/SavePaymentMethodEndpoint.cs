using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PayPal;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IRepository<UserPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request,
                   HttpContext httpContext,
                   IRepository<UserPaymentMethod> pmRepo,
                   IPayPalClient paypal,
                   ILogger<SavePaymentMethodEndpoint> logger) =>
            {
                var userName = httpContext.User.Identity!.Name!;

                if (string.IsNullOrWhiteSpace(request.CardNumber) ||
                    string.IsNullOrWhiteSpace(request.CardExpiry))
                    return Results.BadRequest(new { error = "CardNumber and CardExpiry are required." });

                var customerId = BuildCustomerId(userName);
                var idempotencyKey = $"eshop-vault-{userName}-{Guid.NewGuid():N}";

                var vaultCard = new PayPalVaultCardRequest(
                    Number: request.CardNumber,
                    Expiry: request.CardExpiry,
                    SecurityCode: request.CardCvv,
                    Name: request.CardName,
                    BillingAddress: request.BillingCountry != null
                        ? new PayPalAddress(request.BillingCountry)
                        : null
                );

                try
                {
                    var tokenResp = await paypal.CreateVaultPaymentTokenAsync(vaultCard, customerId, idempotencyKey);

                    var last4 = tokenResp.PaymentSource?.Card?.LastDigits ?? "****";
                    var brand = tokenResp.PaymentSource?.Card?.Brand ?? "UNKNOWN";
                    var expiry = tokenResp.PaymentSource?.Card?.Expiry ?? request.CardExpiry ?? string.Empty;
                    var paypalCustomerId = tokenResp.Customer?.Id ?? customerId;

                    var pm = new UserPaymentMethod(userName, paypalCustomerId, tokenResp.Id, last4, brand, expiry);
                    await pmRepo.AddAsync(pm);

                    return Results.Created($"api/payment-methods/{pm.Id}", new SavePaymentMethodResponse(pm.Id, last4, brand, expiry));
                }
                catch (PayPalException ex)
                {
                    logger.LogError(ex, "PayPal vault token creation failed for user {User}", userName);
                    return Results.UnprocessableEntity(new { error = ex.Message, detail = ex.PayPalErrorBody });
                }
            })
            .Produces<SavePaymentMethodResponse>(201)
            .WithTags("PaymentMethodEndpoints");
    }

    private static string BuildCustomerId(string userName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userName));
        return Convert.ToHexString(hash).ToLowerInvariant()[..22];
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request, IRepository<UserPaymentMethod> service)
        => Task.FromResult(Results.StatusCode(501));
}

public class SavePaymentMethodRequest : BaseRequest
{
    public string? CardNumber { get; set; }
    public string? CardExpiry { get; set; }
    public string? CardCvv { get; set; }
    public string? CardName { get; set; }
    public string? BillingCountry { get; set; }
}

public record SavePaymentMethodResponse(int PaymentMethodId, string Last4, string Brand, string Expiry);
