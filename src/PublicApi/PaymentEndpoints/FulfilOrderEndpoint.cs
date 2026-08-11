using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Carries just the order id for operator actions that take no body.</summary>
public class OrderActionRequest
{
    public int OrderId { get; set; }
}

/// <summary>
/// Operator action: marks the order fulfilled, which is when the money is actually taken (captured). A hold
/// that has gone stale is renewed rather than failing the fulfilment outright. Restricted to administrators.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderActionRequest>
{
    private readonly IPaymentService _paymentService;

    public FulfilOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) => await HandleAsync(new OrderActionRequest { OrderId = orderId }))
            .Produces<PaymentDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request)
    {
        var payment = await _paymentService.FulfilOrderAsync(request.OrderId);
        return Results.Ok(PaymentDto.From(payment));
    }
}
