using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest
{
    public string CardNumber { get; set; } = string.Empty;
    public string CardExpiry { get; set; } = string.Empty;
    public string CardCvc { get; set; } = string.Empty;
    public string? CardHolderName { get; set; }
    public string BillingCountryCode { get; set; } = string.Empty;
    public string? BillingAddressLine1 { get; set; }
    public string? BillingCity { get; set; }
    public string? BillingState { get; set; }
    public string? BillingPostalCode { get; set; }
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Last4 { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, HttpContext ctx) =>
            {
                return await HandleAsync(request, ctx);
            })
            .Produces<CreatePaymentMethodResponse>(201)
            .Produces(400)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, HttpContext ctx)
    {
        if (string.IsNullOrWhiteSpace(request.CardNumber) ||
            string.IsNullOrWhiteSpace(request.CardExpiry) ||
            string.IsNullOrWhiteSpace(request.BillingCountryCode))
        {
            return Results.BadRequest("cardNumber, cardExpiry, and billingCountryCode are required.");
        }

        var buyerId = ctx.User.FindFirstValue(ClaimTypes.Name)!;
        var sp = ctx.RequestServices;
        var savedCardRepo = sp.GetRequiredService<IRepository<SavedCard>>();
        var paypalService = sp.GetRequiredService<IPayPalService>();
        var ct = ctx.RequestAborted;

        // Find existing PayPal customer ID for this buyer (to associate vault tokens)
        var existingCardsSpec = new SavedCardsByBuyerSpec(buyerId);
        var existingCards = await savedCardRepo.ListAsync(existingCardsSpec, ct);
        string? existingPayPalCustomerId = null;
        foreach (var c in existingCards)
        {
            if (c.PayPalCustomerId is not null)
            {
                existingPayPalCustomerId = c.PayPalCustomerId;
                break;
            }
        }

        var cardDetails = new DirectCardDetails(
            Number: request.CardNumber,
            Expiry: request.CardExpiry,
            SecurityCode: request.CardCvc,
            Name: request.CardHolderName,
            CountryCode: request.BillingCountryCode,
            AddressLine1: request.BillingAddressLine1,
            City: request.BillingCity,
            State: request.BillingState,
            PostalCode: request.BillingPostalCode);

        VaultTokenResult vaultResult;
        try
        {
            vaultResult = await paypalService.VaultCardAsync(
                idempotencyKey: $"vault-{buyerId}-{System.Guid.NewGuid():N}",
                card: cardDetails,
                existingPayPalCustomerId: existingPayPalCustomerId,
                merchantCustomerId: buyerId,
                ct: ct);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode ?? 422);
        }

        var savedCard = new SavedCard(buyerId, vaultResult.TokenId, vaultResult.Last4, vaultResult.Brand, vaultResult.Expiry, vaultResult.PayPalCustomerId);
        savedCard = await savedCardRepo.AddAsync(savedCard, ct);

        return Results.Created($"/api/payment-methods/{savedCard.Id}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = savedCard.Id,
            Last4 = savedCard.Last4,
            Brand = savedCard.Brand,
            Expiry = savedCard.Expiry
        });
    }
}
