using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// GET /api/payment-methods — the caller's saved cards (safe descriptors only). Shopper-scoped.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IRepository<SavedPaymentMethod> paymentMethodRepository,
                CancellationToken ct) =>
            {
                var buyerId = PaymentMapping.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var methods = await paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);

                var response = new ListPaymentMethodsResponse
                {
                    PaymentMethods = methods.Select(m => new SavedCardDto
                    {
                        PaymentMethodId = m.Id,
                        Brand = m.Brand,
                        Last4 = m.Last4,
                        Expiry = m.Expiry,
                        Alias = m.Alias,
                        CreatedAt = m.CreatedAt
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}
