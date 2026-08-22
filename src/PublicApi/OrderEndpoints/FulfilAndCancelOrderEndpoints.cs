using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService>
{
    private readonly IPayPalGateway _payPal;

    public FulfilOrderEndpoint(IPayPalGateway payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderPaymentService payments) =>
                await HandleAsync(orderId, payments))
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderPaymentService payments)
    {
        var order = await payments.FulfilOrderAsync(orderId);
        return Results.Ok(new OrderActionResponse
        {
            Order = OrderDto.From(order, _payPal.Currency)
        });
    }
}

public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService>
{
    private readonly IPayPalGateway _payPal;

    public CancelOrderEndpoint(IPayPalGateway payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderPaymentService payments) =>
                await HandleAsync(orderId, payments))
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderPaymentService payments)
    {
        var order = await payments.CancelOrderAsync(orderId);
        return Results.Ok(new OrderActionResponse
        {
            Order = OrderDto.From(order, _payPal.Currency)
        });
    }
}

public class OrderActionResponse : BaseResponse
{
    public OrderDto Order { get; set; } = new();
}
