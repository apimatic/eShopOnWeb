using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: fulfils an order, capturing the held funds. A stale authorization is renewed first.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public FulfilOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) => await HandleAsync(new OrderActionRequest { OrderId = orderId }, service))
            .Produces<OrderPaymentSummary>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IOrderPaymentService service)
    {
        var summary = await service.FulfilAsync(request.OrderId, _http.HttpContext!.RequestAborted);
        return Results.Ok(summary);
    }
}
