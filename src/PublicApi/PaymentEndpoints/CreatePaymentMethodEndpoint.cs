using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreatePaymentMethodRequest
{
    public CardDto Card { get; set; } = new();
    public string? Alias { get; set; }
}

/// <summary>
/// POST /api/payment-methods — save a card for the signed-in shopper. The card is vaulted with PayPal;
/// the response identifies the saved card and describes it safely (brand / last four) — never full details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    private readonly IPaymentMethodService _paymentMethodService;

    public CreatePaymentMethodEndpoint(IPaymentMethodService paymentMethodService)
    {
        _paymentMethodService = paymentMethodService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var method = await _paymentMethodService.SaveCardAsync(
                    buyerId, request.Card.ToCardDetails(), request.Alias, ct);
                return Results.Created($"/api/payment-methods/{method.Id}", method.ToDto());
            })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
