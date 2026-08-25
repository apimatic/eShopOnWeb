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
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IRepository<SavedPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, IRepository<SavedPaymentMethod> repo,
                   IPayPalService paypal, ClaimsPrincipal user, CancellationToken ct) =>
            {
                request.UserId = user.Identity?.Name ?? "";
                return await HandleAsync(request, repo, paypal, ct);
            })
            .Produces<SavePaymentMethodResponse>(201)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IRepository<SavedPaymentMethod> repo)
        => Results.StatusCode(500);

    private async Task<IResult> HandleAsync(SavePaymentMethodRequest request,
        IRepository<SavedPaymentMethod> repo, IPayPalService paypal, CancellationToken ct)
    {
        var idempotencyKey = $"vault-{request.UserId}-{Guid.NewGuid()}";
        // PayPal customer ID only allows [0-9a-zA-Z_-]; sanitize email special chars.
        var safeCustomerId = System.Text.RegularExpressions.Regex.Replace(request.UserId, "[^0-9a-zA-Z_-]", "-");
        if (safeCustomerId.Length > 36) safeCustomerId = safeCustomerId[..36];

        VaultResult vault;
        try
        {
            vault = await paypal.VaultCardAsync(
                customerId: safeCustomerId,
                idempotencyKey: idempotencyKey,
                card: new CardPaymentDetails(
                    Number: request.Number,
                    ExpiryYear: request.ExpiryYear,
                    ExpiryMonth: request.ExpiryMonth,
                    Cvv: request.Cvv,
                    CardholderName: request.CardholderName,
                    Street: request.Street,
                    City: request.City,
                    State: request.State,
                    PostalCode: request.PostalCode,
                    CountryCode: request.CountryCode),
                ct: ct);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(
                title: "Card vault failed",
                detail: ex.Message,
                statusCode: ex.StatusCode);
        }

        var method = new SavedPaymentMethod(
            userId: request.UserId,
            vaultToken: vault.VaultToken,
            last4Digits: vault.Last4Digits,
            cardBrand: vault.CardBrand,
            expiry: vault.Expiry);

        method = await repo.AddAsync(method, ct);

        return Results.Created(
            $"api/payment-methods/{method.Id}",
            new SavePaymentMethodResponse
            {
                PaymentMethodId = method.Id,
                Last4Digits = vault.Last4Digits,
                CardBrand = vault.CardBrand,
                Expiry = vault.Expiry
            });
    }
}
