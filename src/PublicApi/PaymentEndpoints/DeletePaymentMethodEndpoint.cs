using System.Security.Claims;
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
/// DELETE /api/payment-methods/{paymentMethodId} — removes a saved card. Afterwards it no longer
/// appears among the caller's cards and can no longer be used to pay. Shopper-scoped.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService service) =>
            {
                return await HandleAsync(
                    new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId, Caller = CallerContext.From(user) },
                    service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService service)
    {
        await service.DeleteAsync(request.PaymentMethodId, request.Caller.Username);
        return Results.NoContent();
    }
}

public class DeletePaymentMethodRequest
{
    public int PaymentMethodId { get; set; }
    public CallerContext Caller { get; set; } = new(string.Empty, false);
}
