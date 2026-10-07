using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: gives money back on a paid order, in full or in part, never beyond what was paid.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IOrderPaymentService orderPaymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, orderPaymentService);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .Produces<RefundOrderResponse>(StatusCodes.Status200OK)
            .Produces<RefundOrderResponse>(StatusCodes.Status409Conflict)
            .Produces<RefundOrderResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<RefundOrderResponse>(StatusCodes.Status504GatewayTimeout)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        RefundReason? reason = null;
        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            if (!Enum.TryParse<RefundReason>(request.Reason.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return Respond(request, new RefundOrderResult(RefundOrderStatus.Invalid, request.OrderId,
                    $"Unknown reason '{request.Reason}'. Use one of: {string.Join(", ", Enum.GetNames<RefundReason>())}."));
            reason = parsed;
        }

        var result = await orderPaymentService.RefundAsync(request.OrderId, request.Amount, reason,
            string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim(), CancellationToken.None);
        return Respond(request, result);
    }

    private static IResult Respond(RefundOrderRequest request, RefundOrderResult result)
    {
        var response = new RefundOrderResponse(request.CorrelationId())
        {
            OrderId = result.OrderId,
            RefundId = result.RefundId,
            Status = result.Status.ToString(),
            Message = result.Message,
            RefundStatus = result.RefundStatus?.ToString(),
            Amount = result.Amount,
            Currency = result.Currency,
            RemainingRefundable = result.RemainingRefundable,
            PspReference = result.PspReference
        };
        return Results.Json(response, statusCode: StatusCodeFor(result.Status));
    }

    internal static int StatusCodeFor(RefundOrderStatus status) => status switch
    {
        RefundOrderStatus.Accepted => StatusCodes.Status201Created,
        RefundOrderStatus.Replayed => StatusCodes.Status200OK,
        RefundOrderStatus.NotFound => StatusCodes.Status404NotFound,
        RefundOrderStatus.NotRefundable or RefundOrderStatus.InProgress => StatusCodes.Status409Conflict,
        RefundOrderStatus.ExceedsRefundable or RefundOrderStatus.Rejected => StatusCodes.Status422UnprocessableEntity,
        RefundOrderStatus.Invalid => StatusCodes.Status400BadRequest,
        RefundOrderStatus.ProviderUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status504GatewayTimeout
    };
}
