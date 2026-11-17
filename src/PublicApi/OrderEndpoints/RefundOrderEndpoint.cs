using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Exceptions;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Refunds a captured payment, fully or partially. The caller's idempotency key prevents
/// a retried request from refunding twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, OrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, OrderPaymentService service) =>
            {
                return await HandleAsync(request, service);
            })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, OrderPaymentService service)
    {
        var buyerId = GetBuyerId();
        var isAdmin = _httpContextAccessor.HttpContext?.User?.IsInRole(Constants.Roles.ADMINISTRATORS) ?? false;
        var orderId = GetRouteInt("orderId");
        var refund = await service.RefundOrderAsync(buyerId, isAdmin, orderId, request, CancellationToken.None);
        var payment = await service.GetPaymentSummaryAsync(orderId, CancellationToken.None);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            OrderId = orderId,
            RefundId = refund.Id,
            FinixRefundId = refund.FinixRefundId,
            Status = refund.Status,
            Amount = refund.AmountCents / 100m,
            TotalRefunded = payment?.RefundedAmountCents / 100m ?? refund.AmountCents / 100m,
            RemainingRefundable = payment?.RefundableAmountCents / 100m ?? 0m,
            Currency = payment?.Currency ?? service.Currency
        };

        return Results.Ok(response);
    }

    private string GetBuyerId()
    {
        var name = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentOperationException(401, "NO_IDENTITY",
                "The access token does not contain a user identity.");
        }

        return name!;
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
