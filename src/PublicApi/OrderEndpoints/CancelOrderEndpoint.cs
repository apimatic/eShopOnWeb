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
/// Operator action: releases an authorization hold before fulfilment. No money is captured.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CancelOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, OrderPaymentService service) =>
            {
                return await HandleAsync(service);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderPaymentService service)
    {
        var orderId = GetRouteInt("orderId");
        var payment = await service.CancelOrderAsync(orderId, CancellationToken.None);

        var response = new CancelOrderResponse(System.Guid.NewGuid())
        {
            OrderId = orderId,
            PaymentId = payment?.Id,
            Status = payment?.Status.ToString() ?? "Cancelled",
            AuthorizationId = payment?.FinixAuthorizationId,
            AuthorizationStatus = payment?.AuthorizationStatus,
            FundsReleased = payment is not null &&
                            !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId)
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
