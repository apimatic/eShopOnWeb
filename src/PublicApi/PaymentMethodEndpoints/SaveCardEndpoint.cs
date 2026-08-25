using System.Linq;
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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SaveCardRequest
{
    public string Number { get; set; } = "";
    public string Expiry { get; set; } = "";   // YYYY-MM
    public string Name { get; set; } = "";
    public SaveCardBillingAddressRequest? BillingAddress { get; set; }
}

public class SaveCardBillingAddressRequest
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string CountryCode { get; set; } = "US";
}

public class SaveCardResponse
{
    public int PaymentMethodId { get; set; }
    public string LastFour { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Expiry { get; set; } = "";
}

public class SaveCardEndpoint : IEndpoint<IResult, SaveCardRequest, IRepository<SavedCard>>
{
    private readonly IPayPalClient _paypal;

    public SaveCardEndpoint(IPayPalClient paypal)
    {
        _paypal = paypal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SaveCardRequest request, IRepository<SavedCard> cardRepo,
                   HttpContext ctx, CancellationToken ct) =>
            {
                var buyerId = ctx.User.FindFirst(ClaimTypes.Name)?.Value;
                return await HandleAsync(request, cardRepo, buyerId, ct);
            })
            .Produces<SaveCardResponse>(201)
            .Produces(400)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(SaveCardRequest request, IRepository<SavedCard> repository)
        => HandleAsync(request, repository, null);

    private async Task<IResult> HandleAsync(SaveCardRequest request, IRepository<SavedCard> cardRepo,
        string? buyerId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Number) || string.IsNullOrWhiteSpace(request.Expiry))
            return Results.BadRequest(new { error = "Card number and expiry are required." });

        // Retrieve existing PayPal customer ID for this buyer (if they've saved a card before)
        var existingCardsSpec = new SavedCardsByBuyerSpec(buyerId);
        var existingCards = await cardRepo.ListAsync(existingCardsSpec, ct);
        var existingCustomerId = existingCards.FirstOrDefault()?.PayPalCustomerId;

        var card = new CardDetails
        {
            Number = request.Number,
            Expiry = request.Expiry,
            Name = request.Name,
            BillingAddress = request.BillingAddress == null ? null : new CardBillingAddress
            {
                Street = request.BillingAddress.Street,
                City = request.BillingAddress.City,
                State = request.BillingAddress.State,
                ZipCode = request.BillingAddress.ZipCode,
                CountryCode = request.BillingAddress.CountryCode
            }
        };

        var requestId = System.Guid.NewGuid().ToString();
        var setupIdempotencyKey = $"setup-{requestId}";
        var paymentIdempotencyKey = $"payment-{requestId}";

        try
        {
            var setupToken = await _paypal.CreateSetupTokenAsync(card, existingCustomerId, setupIdempotencyKey, ct);
            var paymentToken = await _paypal.CreatePaymentTokenAsync(setupToken.SetupTokenId, paymentIdempotencyKey, ct);

            var customerId = !string.IsNullOrEmpty(paymentToken.CustomerId)
                ? paymentToken.CustomerId
                : setupToken.CustomerId;

            var savedCard = new SavedCard(
                buyerId,
                customerId,
                paymentToken.VaultId,
                paymentToken.LastFour,
                paymentToken.Brand,
                paymentToken.Expiry);

            await cardRepo.AddAsync(savedCard, ct);

            return Results.Created($"/api/payment-methods/{savedCard.Id}", new SaveCardResponse
            {
                PaymentMethodId = savedCard.Id,
                LastFour = savedCard.LastFour,
                Brand = savedCard.Brand,
                Expiry = savedCard.Expiry
            });
        }
        catch (PayPalException ex)
        {
            return Results.BadRequest(new { error = ex.Message, code = ex.PayPalErrorName });
        }
    }
}
