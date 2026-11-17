using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var result = await paymentService.FulfilOrderAsync(orderId, cancellationToken);

                return Results.Ok(new FulfilmentResponse
                {
                    OrderId = result.OrderId,
                    PaymentStatus = result.PaymentStatus,
                    TransferId = result.TransferId,
                    TransferState = result.TransferState,
                    CapturedAmount = result.CapturedAmount,
                    FinixFee = result.FinixFee,
                    NetProceeds = result.NetProceeds,
                    Currency = result.Currency,
                    IdempotentReplay = result.IdempotentReplay
                });
            })
            .Produces<FulfilmentResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }
}
