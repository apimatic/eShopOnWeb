using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IRepository<SavedCard>>
{
    private readonly IPayPalService _paypal;

    public CreatePaymentMethodEndpoint(IPayPalService paypal) => _paypal = paypal;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, ClaimsPrincipal user, IRepository<SavedCard> cardRepo) =>
            {
                request.BuyerId = user.Identity?.Name ?? "";
                return await HandleAsync(request, cardRepo);
            })
            .Produces<CreatePaymentMethodResponse>(201)
            .Produces(400)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IRepository<SavedCard> cardRepo)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        if (string.IsNullOrEmpty(request.Number) || string.IsNullOrEmpty(request.Expiry) || string.IsNullOrEmpty(request.SecurityCode))
            return Results.BadRequest(new { error = "Card number, expiry, and security code are required." });

        try
        {
            var vaultResult = await _paypal.VaultCardAsync(
                new PayPalCardRequest(request.Number, request.Expiry, request.SecurityCode, request.CardholderName),
                merchantCustomerId: request.BuyerId,
                CancellationToken.None);

            var savedCard = new SavedCard(
                request.BuyerId,
                vaultResult.PaymentTokenId,
                vaultResult.Last4,
                vaultResult.Brand,
                vaultResult.Expiry,
                vaultResult.CardholderName);

            savedCard = await cardRepo.AddAsync(savedCard);

            return Results.Created($"api/payment-methods/{savedCard.Id}",
                new CreatePaymentMethodResponse(request.CorrelationId())
                {
                    PaymentMethodId = savedCard.Id,
                    Last4 = savedCard.Last4,
                    Brand = savedCard.Brand,
                    Expiry = savedCard.Expiry,
                    CardholderName = savedCard.CardholderName
                });
        }
        catch (PayPalException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string BuyerId { get; set; } = "";
    public string Number { get; set; } = "";
    public string Expiry { get; set; } = "";
    public string SecurityCode { get; set; } = "";
    public string? CardholderName { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public int PaymentMethodId { get; set; }
    public string? Last4 { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}
