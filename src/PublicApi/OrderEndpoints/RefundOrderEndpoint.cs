using System.Security.Claims;
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
/// Operator action: gives back all or part of a paid order's payment. Refunds can never add up to
/// more than was paid.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest? request, ClaimsPrincipal user, IOrderPaymentService paymentService) =>
            {
                request ??= new RefundOrderRequest();
                request.OrderId = orderId;
                request.RequestedBy = user.Identity!.Name!;
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .Produces<RefundOrderResponse>(StatusCodes.Status202Accepted)
            .Produces<RefundOrderResponse>(StatusCodes.Status400BadRequest)
            .Produces<RefundOrderResponse>(StatusCodes.Status409Conflict)
            .Produces<RefundOrderResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<RefundOrderResponse>(StatusCodes.Status502BadGateway)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService paymentService)
    {
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason?.Length > 256) reason = reason[..256];

        var result = await paymentService.RefundAsync(request.OrderId, request.Amount, reason, request.RequestedBy,
            CancellationToken.None);
        if (result.Outcome == RefundOrderOutcome.OrderNotFound)
        {
            return Results.NotFound();
        }

        var refund = result.Refund;
        var currency = refund?.Currency ?? result.Currency;
        var response = new RefundOrderResponse(request.CorrelationId())
        {
            OrderId = result.OrderId,
            RefundId = refund?.RefundId,
            Outcome = result.Outcome.ToString(),
            RefundStatus = refund?.Status.ToString(),
            Amount = refund is null ? null : MinorUnits.ToDecimal(refund.AmountMinorUnits, refund.Currency),
            Currency = currency,
            PspReference = refund?.PspReference,
            RefundableAmount = currency is null ? 0m : MinorUnits.ToDecimal(result.RefundableMinorUnits, currency),
            Message = result.Message,
        };

        return result.Outcome switch
        {
            RefundOrderOutcome.Received => Results.Created($"api/orders/{result.OrderId}/adyen-record", response),
            RefundOrderOutcome.Pending => Results.Json(response, statusCode: StatusCodes.Status202Accepted),
            RefundOrderOutcome.InvalidAmount => Results.Json(response, statusCode: StatusCodes.Status400BadRequest),
            RefundOrderOutcome.NotPaid or RefundOrderOutcome.InProgress => Results.Json(response, statusCode: StatusCodes.Status409Conflict),
            RefundOrderOutcome.ExceedsRefundable or RefundOrderOutcome.Rejected => Results.Json(response, statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Json(response, statusCode: StatusCodes.Status502BadGateway)
        };
    }
}
