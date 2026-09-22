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

/// <summary>Removes one of the caller's saved cards; afterwards it can no longer be used to pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, PaymentMethodOperationRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
            {
                return await HandleAsync(
                    new PaymentMethodOperationRequest { PaymentMethodId = paymentMethodId, BuyerId = user.Identity?.Name, Cancellation = ct },
                    service);
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(PaymentMethodOperationRequest request, IPaymentMethodService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var deleted = await service.DeleteAsync(request.BuyerId, request.PaymentMethodId, request.Cancellation);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
