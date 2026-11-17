using System;
using System.Threading;
using System.Threading.Tasks;
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
/// Authorizes (holds) the order total using either a saved card or one-off card details.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, OrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, OrderPaymentService service) =>
            {
                return await HandleAsync(request, service);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, OrderPaymentService service)
    {
        var buyerId = GetBuyerId();
        var orderId = GetRouteInt("orderId");
        var payment = await service.PayOrderAsync(buyerId, orderId, request, CancellationToken.None);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = orderId,
            PaymentId = payment.Id,
            Status = payment.Status.ToString(),
            AuthorizationId = payment.FinixAuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            Amount = payment.AuthorizedAmountCents / 100m,
            Currency = payment.Currency
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
