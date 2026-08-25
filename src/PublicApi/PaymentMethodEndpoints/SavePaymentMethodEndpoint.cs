using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IRepository<SavedPaymentMethod>>
{
    private readonly PayPalClient _payPalClient;

    public SavePaymentMethodEndpoint(PayPalClient payPalClient)
    {
        _payPalClient = payPalClient;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, IRepository<SavedPaymentMethod> repository, ClaimsPrincipal user) =>
            {
                var buyerId = user.FindFirst(ClaimTypes.Name)?.Value ?? "";
                return await HandleAsync(request with { BuyerId = buyerId }, repository);
            })
            .Produces<SavePaymentMethodResponse>(201)
            .Produces(400)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IRepository<SavedPaymentMethod> repository)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        if (string.IsNullOrEmpty(request.CardNumber) || string.IsNullOrEmpty(request.CardExpiry) ||
            string.IsNullOrEmpty(request.CardCvv))
            return Results.BadRequest(new { error = "CardNumber, CardExpiry, and CardCvv are required." });

        var billingAddress = string.IsNullOrEmpty(request.BillingCountryCode) ? null :
            new PayPalAddress
            {
                CountryCode = request.BillingCountryCode,
                AddressLine1 = request.BillingStreet,
                City = request.BillingCity,
                State = request.BillingState,
                PostalCode = request.BillingPostalCode
            };

        var idempotencyKey = Guid.NewGuid().ToString();

        try
        {
            var vaultResult = await _payPalClient.CreateVaultPaymentTokenAsync(
                request.CardNumber, request.CardExpiry, request.CardCvv,
                request.CardName ?? "", billingAddress, request.BuyerId, idempotencyKey);

            var card = vaultResult.PaymentSource?.Card;
            var savedMethod = new SavedPaymentMethod(
                request.BuyerId,
                vaultResult.Id,
                card?.Brand,
                card?.LastDigits,
                card?.Expiry,
                card?.Name ?? request.CardName);

            savedMethod = await repository.AddAsync(savedMethod);

            return Results.Created($"api/payment-methods/{savedMethod.Id}", new SavePaymentMethodResponse
            {
                PaymentMethodId = savedMethod.Id,
                CardBrand = savedMethod.CardBrand,
                Last4 = savedMethod.Last4,
                CardExpiry = savedMethod.CardExpiry,
                CardholderName = savedMethod.CardholderName
            });
        }
        catch (PayerActionRequiredException ex)
        {
            return Results.Problem(
                detail: $"Card requires browser-based approval (3DS). This is not supported headlessly: {ex.ApprovalUrl}",
                statusCode: 422,
                title: "PayerActionRequired");
        }
        catch (PayPalException ex)
        {
            return Results.Problem(
                detail: ex.Message,
                statusCode: ex.StatusCode > 0 ? ex.StatusCode : 502,
                title: "PayPalError",
                extensions: ex.DebugId != null
                    ? new System.Collections.Generic.Dictionary<string, object?> { ["debugId"] = ex.DebugId }
                    : null);
        }
    }
}
