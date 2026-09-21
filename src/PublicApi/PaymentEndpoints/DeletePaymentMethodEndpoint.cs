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
/// DELETE /api/payment-methods/{paymentMethodId} — remove a saved card. Afterwards it no longer
/// appears among the caller's saved cards and can no longer be used to pay. Scoped to the owner.
/// </summary>
public class DeletePaymentMethodEndpoint : PaymentEndpointBase, IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentService>
{
    public DeletePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, IPaymentService service) =>
                await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId), service))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentService service)
    {
        await service.DeleteSavedCardAsync(BuyerId, request.PaymentMethodId, RequestAborted);
        return Results.NoContent();
    }
}
