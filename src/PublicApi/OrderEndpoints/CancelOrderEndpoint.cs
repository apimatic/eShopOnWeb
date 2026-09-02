using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Helpers;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels the order before fulfilment and releases the shopper's
/// held funds, so no money ever moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly IOrderPaymentService _paymentService;

    public CancelOrderEndpoint(IOrderPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext) =>
            {
                return await HandleAsync(orderId, httpContext);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, HttpContext httpContext)
    {
        try
        {
            var payment = await _paymentService.CancelAsync(orderId);

            return Results.Ok(new CancelOrderResponse
            {
                OrderId = orderId,
                Status = "Cancelled",
                PaymentStatus = payment?.Status.ToString(),
                AuthorizationId = payment?.AuthorizationId
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EndpointHelpers.MapException(ex);
        }
    }
}

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? PaymentStatus { get; set; }
    public string? AuthorizationId { get; set; }
}
