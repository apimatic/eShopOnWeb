using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>A payment action that only needs the order id (fulfil / cancel).</summary>
public class OrderActionRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class PaymentActionResponse : BaseResponse
{
    public PaymentDto Payment { get; set; } = new();
}

/// <summary>Operator action: fulfils the order and captures the held funds.</summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public FulfilOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
                await HandleAsync(new OrderActionRequest { OrderId = orderId }, paymentService))
            .Produces<PaymentActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IPaymentService paymentService)
    {
        var payment = await paymentService.FulfilAsync(request.OrderId, _http.HttpContext!.RequestAborted);
        return Results.Ok(new PaymentActionResponse() { Payment = payment.ToDto() });
    }
}
