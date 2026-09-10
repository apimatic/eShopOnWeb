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

public record DeletePaymentMethodCommand(string BuyerId, string PaymentMethodId, CancellationToken Ct);

/// <summary>
/// DELETE /api/payment-methods/{paymentMethodId} — remove a saved card. Afterwards it no longer appears
/// among the caller's cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodCommand, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string paymentMethodId, HttpContext http, IPaymentApplicationService service) =>
            {
                var buyerId = CallerContext.GetBuyerId(http);
                if (buyerId is null) return Results.Unauthorized();
                return await HandleAsync(new DeletePaymentMethodCommand(buyerId, paymentMethodId, http.RequestAborted), service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodCommand command, IPaymentApplicationService service)
    {
        await service.DeleteCardAsync(command.BuyerId, command.PaymentMethodId, command.Ct);
        return Results.NoContent();
    }
}
