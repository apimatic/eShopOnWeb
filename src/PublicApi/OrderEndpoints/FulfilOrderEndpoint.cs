using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using BlazorShared.Authorization;
using Microsoft.eShopWeb.PublicApi.Exceptions;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: captures the previously authorized hold and marks the order fulfilled.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FulfilOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderPaymentService service) =>
            {
                return await HandleAsync(service);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderPaymentService service)
    {
        var orderId = GetRouteInt("orderId");
        var payment = await service.FulfilOrderAsync(orderId, CancellationToken.None);

        var response = new FulfilOrderResponse(System.Guid.NewGuid())
        {
            OrderId = orderId,
            PaymentId = payment.Id,
            Status = payment.Status.ToString(),
            CaptureId = payment.FinixCaptureTransferId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmountCents / 100m,
            Fee = payment.FeeCents / 100m,
            Net = payment.NetCents / 100m,
            Currency = payment.Currency,
            OperatorMessage = string.Equals(payment.CaptureStatus, "PENDING", System.StringComparison.OrdinalIgnoreCase)
                ? "Finix accepted the capture and is still settling it. The fee and net values will refresh on GET /api/my-orders."
                : null
        };

        return Results.Ok(response);
    }

    private int GetRouteInt(string name)
    {
        var value = _httpContextAccessor.HttpContext?.GetRouteValue(name)?.ToString();
        if (!int.TryParse(value, out var result))
        {
            throw new PaymentOperationException(400, "INVALID_ROUTE", $"The route value '{name}' is invalid.");
        }

        return result;
    }
}
