using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Lists the signed-in shopper's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(buyerId))
                {
                    throw new PaymentException(401, "A signed-in shopper is required.");
                }

                var savedCards = await paymentService.GetPaymentMethodsAsync(buyerId, cancellationToken);
                var response = new ListPaymentMethodsResponse(System.Guid.NewGuid());
                response.PaymentMethods = savedCards.Select(saved => new PaymentMethodDto
                {
                    PaymentMethodId = saved.Id,
                    Brand = saved.Brand,
                    Last4 = saved.Last4,
                    ExpirationMonth = saved.ExpirationMonth,
                    ExpirationYear = saved.ExpirationYear,
                    CardType = saved.CardType,
                    Name = saved.Name,
                    CreatedAt = saved.CreatedAt
                }).ToList();

                return Results.Ok(response);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}
