using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, OrderIdRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPaymentService payments) =>
            {
                return await HandleAsync(new OrderIdRequest { OrderId = orderId }, payments);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request, IPaymentService payments)
    {
        var result = await payments.FulfilAsync(request.OrderId);
        return Results.Ok(OrderResponseMapper.From(result));
    }
}

public class CancelOrderEndpoint : IEndpoint<IResult, OrderIdRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPaymentService payments) =>
            {
                return await HandleAsync(new OrderIdRequest { OrderId = orderId }, payments);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request, IPaymentService payments)
    {
        var result = await payments.CancelAsync(request.OrderId);
        return Results.Ok(OrderResponseMapper.From(result));
    }
}

public class OrderIdRequest : BaseRequest
{
    public int OrderId { get; set; }
}
