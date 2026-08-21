using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Lists the signed-in shopper's saved cards. Shopper-scoped.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService paymentService, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.GetBuyerId(user);
                var cards = await paymentService.GetCardsForBuyerAsync(buyerId, ct);

                var response = new ListPaymentMethodsResponseDto
                {
                    PaymentMethods = cards.Select(c => new PaymentMethodDto
                    {
                        PaymentMethodId = c.Id,
                        Brand = c.Brand,
                        LastDigits = c.LastDigits,
                        Expiry = c.Expiry,
                        CardholderName = c.CardholderName,
                        CreatedAt = c.CreatedAt
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListPaymentMethodsResponseDto>()
            .WithTags("PaymentMethodEndpoints");
    }
}
