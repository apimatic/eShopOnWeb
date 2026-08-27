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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Lists the caller's saved cards (safe display data only).
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService orderPaymentService, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null)
                {
                    return Results.Unauthorized();
                }

                var methods = await orderPaymentService.ListCardsAsync(buyerId, ct);

                var response = new ListPaymentMethodsResponse
                {
                    PaymentMethods = methods.Select(m => new SavedPaymentMethodDto
                    {
                        PaymentMethodId = m.Id,
                        Brand = m.Brand,
                        LastDigits = m.LastDigits,
                        Expiry = m.Expiry,
                        CardholderName = m.CardholderName,
                        CreatedAt = m.CreatedAt
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}
