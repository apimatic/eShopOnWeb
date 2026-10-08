using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: gives money back on a paid order, in full or in part. Never beyond what was paid.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public const int MaxIdempotencyKeyLength = 128;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest? request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, ClaimsPrincipal user, IOrderPaymentService orderPaymentService) =>
            {
                request ??= new RefundOrderRequest();
                request.OrderId = orderId;
                request.IdempotencyKey = idempotencyKey;
                request.RequestedBy = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .Produces<RefundOrderResponse>()
            .Produces<RefundOrderResponse>(StatusCodes.Status400BadRequest)
            .Produces<RefundOrderResponse>(StatusCodes.Status404NotFound)
            .Produces<RefundOrderResponse>(StatusCodes.Status409Conflict)
            .Produces<RefundOrderResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<RefundOrderResponse>(StatusCodes.Status502BadGateway)
            .Produces<RefundOrderResponse>(StatusCodes.Status504GatewayTimeout)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var response = new RefundOrderResponse(request.CorrelationId()) { OrderId = request.OrderId };
        if (request.IdempotencyKey is { Length: > MaxIdempotencyKeyLength })
        {
            response.Outcome = "InvalidRequest";
            response.Message = $"The Idempotency-Key header must be at most {MaxIdempotencyKeyLength} characters.";
            return Results.BadRequest(response);
        }

        var result = await orderPaymentService.RefundOrderAsync(new RefundOrderCommand(
            request.OrderId, request.Amount, request.Reason?.Trim(), request.IdempotencyKey, request.RequestedBy));

        response.Outcome = result.Outcome.ToString();
        response.Message = result.Message;
        if (result.Refund is { } refund)
        {
            response.RefundId = refund.Id;
            response.Refund = RefundDto.From(refund);
        }
        if (result.Order is { } order)
        {
            response.Payment = PaymentDto.From(order, null);
        }

        if (result.Outcome == RefundOrderOutcome.Submitted)
            return Results.Created($"api/orders/{request.OrderId}/refunds/{response.RefundId}", response);

        var status = result.Outcome switch
        {
            RefundOrderOutcome.AlreadySubmitted => StatusCodes.Status200OK,
            RefundOrderOutcome.OrderNotFound => StatusCodes.Status404NotFound,
            RefundOrderOutcome.InvalidAmount => StatusCodes.Status400BadRequest,
            RefundOrderOutcome.OrderNotPaid or RefundOrderOutcome.RefundInProgress => StatusCodes.Status409Conflict,
            RefundOrderOutcome.ExceedsRefundable or RefundOrderOutcome.IdempotencyKeyReused or RefundOrderOutcome.Rejected => StatusCodes.Status422UnprocessableEntity,
            RefundOrderOutcome.ProcessorTimeout => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status502BadGateway
        };
        return Results.Json(response, statusCode: status);
    }
}
