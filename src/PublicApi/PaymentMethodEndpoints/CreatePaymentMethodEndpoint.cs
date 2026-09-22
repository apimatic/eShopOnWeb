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

/// <summary>Saves (vaults) a card for the signed-in shopper. Returns a safe description, never card details.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
            {
                request.BuyerId = user.Identity?.Name;
                request.Cancellation = ct;
                return await HandleAsync(request, service);
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var saved = await service.SaveCardAsync(request.BuyerId, request.Card.ToCardDetails(), request.Cancellation);
        return Results.Created(
            $"/api/payment-methods/{saved.Id}",
            new
            {
                paymentMethodId = saved.Id,
                brand = saved.Brand,
                last4 = saved.Last4,
                expiry = saved.Expiry,
                cardholderName = saved.CardholderName,
            });
    }
}
