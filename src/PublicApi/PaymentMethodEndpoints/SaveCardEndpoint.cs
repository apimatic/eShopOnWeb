using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SaveCardEndpoint : IEndpoint
{
    private readonly IRepository<SavedCard> _cardRepo;
    private readonly IPayPalGateway _paypal;

    public SaveCardEndpoint(IRepository<SavedCard> cardRepo, IPayPalGateway paypal)
    {
        _cardRepo = cardRepo;
        _paypal = paypal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx, SaveCardRequest request) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(request, buyerId, ctx.RequestAborted);
            })
            .Produces<SaveCardResponse>(201)
            .ProducesProblem(400)
            .WithTags("PaymentMethodEndpoints");
    }

    private async Task<IResult> HandleAsync(SaveCardRequest request, string buyerId, System.Threading.CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Number) || string.IsNullOrWhiteSpace(request.Expiry))
            return Results.BadRequest("Card number and expiry are required.");

        VaultResult vaultResult;
        try
        {
            var cardDetails = new CardDetails(request.Name, request.Number, request.Expiry, request.SecurityCode ?? string.Empty);
            vaultResult = await _paypal.VaultCardAsync(buyerId, cardDetails, ct);
        }
        catch (PayPalException ex) when (ex.Kind == PayPalErrorKind.PayerActionRequired)
        {
            return Results.Problem(ex.Message, statusCode: 422);
        }
        catch (PayPalException ex)
        {
            return Results.Problem($"Failed to save card: {ex.Message}", statusCode: 502);
        }

        var card = new SavedCard(buyerId, vaultResult.VaultId, vaultResult.PayPalCustomerId,
            vaultResult.Last4, vaultResult.Brand, vaultResult.Expiry);
        card = await _cardRepo.AddAsync(card, ct);

        return Results.Created($"/api/payment-methods/{card.Id}", new SaveCardResponse
        {
            PaymentMethodId = card.Id,
            Last4 = card.Last4,
            Brand = card.CardBrand,
            Expiry = card.Expiry
        });
    }
}

public class SaveCardRequest
{
    public string? Name { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
}

public class SaveCardResponse
{
    public int PaymentMethodId { get; set; }
    public string? Last4 { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}
