using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator marks the order fulfilled — this is when the held money is actually
/// captured. A stale authorization is renewed first instead of failing the fulfilment.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    private readonly IOrderPaymentService _payments;

    public FulfilOrderEndpoint(IOrderPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId) =>
            {
                return await HandleAsync(orderId);
            })
            .Produces<FulfilOrderResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        var result = await _payments.FulfilAsync(orderId);
        var order = result.Order;

        var response = new FulfilOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Replayed = result.Replayed,
            Capture = order.Payment?.CaptureId is { Length: > 0 } captureId
                ? new PaymentDtos.CaptureDto
                {
                    CaptureId = captureId,
                    Status = order.Payment.CaptureStatus,
                    AmountCaptured = order.Payment.CapturedAmount ?? 0m,
                    FeeAmount = order.Payment.FeeAmount,
                    NetAmount = order.Payment.NetAmount,
                    Currency = order.Payment.CurrencyCode,
                    CapturedAt = order.Payment.CapturedAt
                }
                : null,
            RemainingRefundableAmount = order.RemainingRefundableAmount()
        };

        return Results.Ok(response);
    }
}
