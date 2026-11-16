using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public sealed class RefundOrderRequest
{
    /// <summary>Amount in cents. Omit for a full refund of the remaining captured amount.</summary>
    public long? AmountInCents { get; set; }

    /// <summary>Optional decimal amount in the configured currency. Omit to use AmountInCents.</summary>
    public decimal? Amount { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>
/// Operator action: refunds a captured payment, in full or in part.
/// </summary>
public sealed class RefundOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                return await PaymentEndpointHelpers.ExecuteAsync(async () =>
                {
                    long? amountInCents = request.AmountInCents;

                    if (request.Amount.HasValue && amountInCents.HasValue)
                    {
                        throw new PaymentOperationException(PaymentErrorKind.Validation,
                            "Send either amountInCents or amount, not both.");
                    }

                    if (request.Amount.HasValue)
                    {
                        amountInCents = (long)Math.Round(request.Amount.Value * 100m,
                            MidpointRounding.AwayFromZero);
                    }

                    var refund = await paymentService.RefundOrderAsync(orderId, amountInCents,
                        request.IdempotencyKey, cancellationToken);

                    return Results.Ok(refund);
                });
            })
            .Produces<RefundInfo>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status402PaymentRequired)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("OrderEndpoints");
    }
}
