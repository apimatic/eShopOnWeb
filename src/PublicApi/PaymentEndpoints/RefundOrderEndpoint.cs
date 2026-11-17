using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, CancellationToken>
{
    private readonly PaymentService _paymentService;

    public RefundOrderEndpoint(PaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, RefundOrderRequest request, CancellationToken cancellationToken) =>
                {
                    request.OrderId = orderId;
                    return await HandleAsync(request, cancellationToken);
                })
            .Produces<RefundResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, CancellationToken cancellationToken)
    {
        var response = await _paymentService.RefundOrderAsync(request.OrderId, request, cancellationToken);
        return Results.Ok(response);
    }
}
