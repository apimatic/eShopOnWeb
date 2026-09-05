using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Returns a fulfilled order's money to the shopper, in full or in part. The caller supplies an
/// idempotency key, so repeating the same request can never return money twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentProcessingService>
{
    /// <summary>Where a caller can put the refund idempotency key instead of in the body.</summary>
    public const string IDEMPOTENCY_KEY_HEADER = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, HttpContext http, ClaimsPrincipal caller,
                IPaymentProcessingService payments) =>
            {
                request.OrderId = orderId;
                request.Actor = RequestActor.From(caller);
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    request.IdempotencyKey = http.Request.Headers[IDEMPOTENCY_KEY_HEADER].ToString();
                }
                return await HandleAsync(request, payments);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentProcessingService payments)
    {
        var actor = request.RequireActor();
        var response = new RefundOrderResponse(request.CorrelationId()) { OrderId = request.OrderId };

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new
            {
                message = "A refund must carry an idempotency key, either as idempotencyKey in the body or as the " +
                          IDEMPOTENCY_KEY_HEADER + " header. Repeating a request under the same key returns the " +
                          "refund that was already made rather than making another one."
            });
        }

        var result = await payments.RefundAsync(actor.BuyerId, request.OrderId, request.Amount,
            request.IdempotencyKey.Trim(), request.NoteToShopper);

        response.RefundId = result.Refund.Id;
        response.AlreadyRecorded = result.AlreadyRecorded;
        response.Refund = RefundDto.From(result.Refund);
        response.Payment = PaymentDto.From(result.Payment);
        response.RefundableAmount = result.Payment.RefundableAmount;

        return result.AlreadyRecorded
            ? Results.Ok(response)
            : Results.Created($"api/orders/{request.OrderId}/refunds/{response.RefundId}", response);
    }
}
