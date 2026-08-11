using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Saves (vaults) a card for the signed-in shopper. The response never carries full card details.</summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                SavePaymentMethodRequest request,
                ClaimsPrincipal user,
                ISavedCardService service,
                CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var saved = await service.SaveCardAsync(buyerId, request.Card.ToCardDetails(), request.Label, ct);

                var response = new SavePaymentMethodResponse
                {
                    PaymentMethodId = saved.PaymentMethodId,
                    Brand = saved.Brand,
                    Last4 = saved.Last4,
                    Expiry = saved.Expiry,
                    Label = saved.Label
                };
                return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", response);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
