using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, OrderIdRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPaymentSettings _paymentSettings;

    public CancelOrderEndpoint(IHttpContextAccessor httpContextAccessor, IPaymentSettings paymentSettings)
    {
        _httpContextAccessor = httpContextAccessor;
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new OrderIdRequest { OrderId = orderId }, paymentService);
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request, IOrderPaymentService paymentService)
    {
        var order = await paymentService.CancelAsync(request.OrderId, _httpContextAccessor.HttpContext!.RequestAborted);
        return Results.Ok(new OrderActionResponse
        {
            Order = OrderDto.From(order, _paymentSettings.Currency)
        });
    }
}
