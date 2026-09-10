using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

/// <summary>Removes a saved card. Afterwards it is neither listed nor usable to pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext http, ISavedCardService service, CancellationToken ct) =>
            {
                return await HandleAsync(
                    new DeletePaymentMethodRequest
                    {
                        PaymentMethodId = paymentMethodId,
                        BuyerId = CallerIdentity.BuyerId(http),
                        Ct = ct,
                    }, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PayPalPaymentMethods");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService service)
    {
        await service.DeleteCardAsync(request.BuyerId, request.PaymentMethodId, request.Ct);
        return Results.NoContent();
    }
}
