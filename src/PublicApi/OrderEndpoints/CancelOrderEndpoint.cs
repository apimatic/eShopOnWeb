using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels the order before fulfilment, releasing the shopper's
/// held funds so no money ever moves. Repeating the call is safe.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IPaymentService _paymentService;

    public CancelOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(orderId);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        var payment = await _paymentService.CancelAsync(orderId);

        var response = new CancelOrderResponse
        {
            OrderId = orderId,
            OrderStatus = "Cancelled",
            PaymentId = payment?.Id,
            PaymentStatus = payment?.Status.ToString(),
            AuthorizationId = payment?.AuthorizationId,
            AuthorizationStatus = payment?.AuthorizationStatus,
            FundsReleased = payment is null || payment.Status == PaymentStatus.Voided
        };
        return Results.Ok(response);
    }
}

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public int? PaymentId { get; set; }
    public string? PaymentStatus { get; set; }
    public string? AuthorizationId { get; set; }
    public string? AuthorizationStatus { get; set; }
    public bool FundsReleased { get; set; }
}
