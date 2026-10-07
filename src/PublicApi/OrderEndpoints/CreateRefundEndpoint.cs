using System.Security.Claims;
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
/// Operator action: gives money back on a paid order, in full or in part. Never beyond what was paid.
/// </summary>
public class CreateRefundEndpoint : IEndpoint<IResult, CreateRefundRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, CreateRefundRequest? request, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                request ??= new CreateRefundRequest();
                request.OrderId = orderId;
                request.RequestedBy = user.Identity!.Name!;
                return await HandleAsync(request, paymentService);
            })
            .Produces<CreateRefundResponse>(StatusCodes.Status201Created)
            .Produces<PaymentErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<PaymentErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<PaymentErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<PaymentErrorResponse>(StatusCodes.Status504GatewayTimeout)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateRefundRequest request, IOrderPaymentService paymentService)
    {
        var result = await paymentService.RefundAsync(request.OrderId, request.Amount, request.RequestedBy);

        var statusCode = result.Outcome switch
        {
            RefundOrderOutcome.Received => StatusCodes.Status201Created,
            RefundOrderOutcome.NotFound => StatusCodes.Status404NotFound,
            RefundOrderOutcome.Invalid => StatusCodes.Status400BadRequest,
            RefundOrderOutcome.ExceedsRefundable or RefundOrderOutcome.Rejected => StatusCodes.Status422UnprocessableEntity,
            RefundOrderOutcome.Busy or RefundOrderOutcome.NotPaid or RefundOrderOutcome.PreviousRefundUnsettled => StatusCodes.Status409Conflict,
            RefundOrderOutcome.ProviderTimeout => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status502BadGateway
        };

        if (result.Outcome == RefundOrderOutcome.Received)
        {
            var refund = result.Refund!;
            var order = result.Order!;
            var response = new CreateRefundResponse(request.CorrelationId())
            {
                RefundId = refund.Id,
                OrderId = order.Id,
                Amount = MinorUnits.FromMinor(refund.AmountMinor, refund.Currency),
                Currency = refund.Currency,
                Status = refund.Status.ToString(),
                PspReference = refund.PspReference,
                PaymentStatus = order.PaymentStatus.ToString(),
                RemainingRefundable = MinorUnits.FromMinor(order.RefundableAmountMinor, refund.Currency),
                Message = result.Message ?? string.Empty
            };
            return Results.Created($"api/orders/{order.Id}/refunds/{refund.Id}", response);
        }

        return Results.Json(new PaymentErrorResponse
        {
            StatusCode = statusCode,
            Message = result.Message ?? "The refund could not be completed.",
            OrderId = request.OrderId,
            PaymentStatus = result.Order?.PaymentStatus.ToString(),
            RefundId = result.Refund?.Id
        }, statusCode: statusCode);
    }
}
