using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Gives money back on a paid order, in full or in part. Operators only. Never refunds beyond what was paid.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    private const int MaxReasonLength = 256;
    private const int MaxIdempotencyKeyLength = 128;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKeyHeader,
                ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.RequestedBy = user.Identity?.Name ?? string.Empty;
                request.IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? idempotencyKeyHeader : request.IdempotencyKey;
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .Produces<RefundOrderResponse>()
            .Produces<RefundOrderResponse>(StatusCodes.Status202Accepted)
            .Produces<RefundOrderResponse>(StatusCodes.Status409Conflict)
            .Produces<RefundOrderResponse>(StatusCodes.Status422UnprocessableEntity)
            .ProducesValidationProblem()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService paymentService)
    {
        if (string.IsNullOrEmpty(request.RequestedBy))
            return Results.Unauthorized();

        var errors = new Dictionary<string, string[]>();
        if (request.Amount is <= 0)
            errors["amount"] = new[] { "amount must be greater than zero (omit it to refund everything still refundable)." };
        if (request.Reason is { Length: > MaxReasonLength })
            errors["reason"] = new[] { $"reason must be at most {MaxReasonLength} characters." };
        if (request.IdempotencyKey is { Length: > MaxIdempotencyKeyLength })
            errors["idempotencyKey"] = new[] { $"idempotencyKey must be at most {MaxIdempotencyKeyLength} characters." };
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        RefundOrderResult result;
        try
        {
            result = await paymentService.RefundAsync(request.OrderId, request.Amount, request.Reason?.Trim(),
                request.IdempotencyKey?.Trim(), request.RequestedBy);
        }
        catch (PaymentConcurrencyException)
        {
            return Results.Json(new RefundOrderResponse(request.CorrelationId())
            {
                OrderId = request.OrderId,
                Outcome = RefundOrderOutcome.InProgress.ToString(),
                Message = "This order is being updated by another request. Please try again in a moment.",
            }, statusCode: StatusCodes.Status409Conflict);
        }

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            OrderId = result.OrderId,
            RefundId = result.Refund?.Id,
            Outcome = result.Outcome.ToString(),
            PaymentStatus = result.PaymentStatus?.ToString(),
            RemainingRefundable = result.RefundableMinor is { } remaining && result.Refund is { } refund
                ? Money.FromMinorUnits(remaining, refund.Currency)
                : null,
            Message = result.Message,
            Refund = result.Refund is null ? null : OrderDtoMapper.ToDto(result.Refund),
        };

        var statusCode = result.Outcome switch
        {
            RefundOrderOutcome.Refunded => StatusCodes.Status201Created,
            RefundOrderOutcome.Existing => StatusCodes.Status200OK,
            RefundOrderOutcome.Unknown => StatusCodes.Status202Accepted,
            RefundOrderOutcome.InProgress or RefundOrderOutcome.NotPaid => StatusCodes.Status409Conflict,
            RefundOrderOutcome.Rejected or RefundOrderOutcome.Invalid => StatusCodes.Status422UnprocessableEntity,
            RefundOrderOutcome.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status502BadGateway,
        };
        return statusCode == StatusCodes.Status201Created
            ? Results.Created($"api/orders/{result.OrderId}/adyen-record", response)
            : Results.Json(response, statusCode: statusCode);
    }
}
