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
    public CardRequest Card { get; set; } = new();
}

/// <summary>
/// Saves a card for the signed-in shopper (vaulted at PayPal). The response identifies the saved
/// card and describes it safely — brand, last four digits, expiry — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreatePaymentMethodRequest request,
                ClaimsPrincipal user,
                IPaymentMethodService paymentMethodService,
                CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null) return Results.Unauthorized();

                if (request.Card is null) return Results.BadRequest(new { errors = new[] { "Card details are required." } });

                var result = await paymentMethodService.SaveCardAsync(buyerId, request.Card.ToCardDetails(), ct);
                if (!result.IsSuccess) return result.ToProblem();

                var dto = result.Value.ToDto();
                return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", new
                {
                    paymentMethodId = dto.PaymentMethodId,
                    card = dto
                });
            })
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .WithTags("PaymentMethodEndpoints");
    }
}
