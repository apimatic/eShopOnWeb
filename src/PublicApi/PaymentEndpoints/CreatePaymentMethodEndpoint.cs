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
/// POST api/payment-methods — saves a card for the signed-in shopper (tokenised in the PayPal
/// vault). Returns the saved card's identifier as a top-level <c>paymentMethodId</c>, plus a safe
/// descriptor (brand + last 4 + expiry) — never full card details.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodRequest request,
                ClaimsPrincipal user,
                IPaymentMethodService paymentMethodService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var saved = await paymentMethodService.SaveCardAsync(buyerId, request.ToCardDetails(), cancellationToken);

                var response = new SavePaymentMethodResponse
                {
                    PaymentMethodId = saved.Id,
                    Brand = saved.Brand,
                    Last4 = saved.Last4,
                    Expiry = saved.ExpiryMonthYear,
                    Alias = saved.Alias
                };

                return Results.Created($"api/payment-methods/{saved.Id}", response);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
