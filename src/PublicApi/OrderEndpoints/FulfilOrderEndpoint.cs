using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, int>
{
    private readonly IOrderPaymentService _paymentService;
    private readonly IPaymentCurrencyAccessor _currency;

    public FulfilOrderEndpoint(IOrderPaymentService paymentService, IPaymentCurrencyAccessor currency)
    {
        _paymentService = paymentService;
        _currency = currency;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) => await HandleAsync(orderId))
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        var order = await _paymentService.FulfilAsync(orderId);
        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.FromOrder(order, _currency.Currency)
        });
    }
}
