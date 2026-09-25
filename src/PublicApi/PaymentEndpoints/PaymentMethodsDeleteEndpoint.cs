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

public record DeletePaymentMethodRequest(string BuyerId, string PaymentMethodId, CancellationToken Ct);

/// <summary>DELETE /api/payment-methods/{paymentMethodId} — remove a saved card (no longer listable or usable to pay).</summary>
public class PaymentMethodsDeleteEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                string paymentMethodId, ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
                await HandleAsync(new DeletePaymentMethodRequest(user.BuyerId(), paymentMethodId, ct), service))
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService service)
    {
        var deleted = await service.DeleteAsync(request.BuyerId, request.PaymentMethodId, request.Ct);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
