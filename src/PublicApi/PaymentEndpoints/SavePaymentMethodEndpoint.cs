using System;
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

/// <summary>
/// POST /api/payment-methods — saves a card for the signed-in shopper. The response identifies the
/// saved card and describes it safely (brand, last four, expiry) — never full card details.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodRequest request,
                ClaimsPrincipal user,
                IPaymentMethodService service,
                CancellationToken ct) =>
            {
                if (request?.Card is null || string.IsNullOrWhiteSpace(request.Card.Number))
                {
                    return Results.BadRequest(new { message = "Card details are required to save a card." });
                }

                try
                {
                    var saved = await service.SaveCardAsync(user.GetBuyerId(), request.Card.ToCardDetails(), request.Alias, ct);
                    var dto = PaymentMethodDto.From(saved);
                    return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", new
                    {
                        paymentMethodId = dto.PaymentMethodId,
                        paymentMethod = dto
                    });
                }
                catch (Exception ex)
                {
                    return PaymentProblems.ToResult(ex);
                }
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
