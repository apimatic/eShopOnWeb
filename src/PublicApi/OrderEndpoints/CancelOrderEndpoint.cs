using System;
using System.Security.Claims;
using System.Threading;
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
/// Operator: cancels an order before fulfilment. Any held funds are released — no money moves.
/// Repeating the call on a cancelled order is a no-op.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, ClaimsPrincipal>
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
            (int orderId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, user);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, ClaimsPrincipal user)
    {
        var order = await _paymentService.CancelOrderAsync(request.OrderId, CancellationToken.None);

        var response = new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        };
        return Results.Ok(response);
    }
}

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
