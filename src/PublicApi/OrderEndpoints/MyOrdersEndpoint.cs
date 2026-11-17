using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersRequest : BaseRequest
{
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) {}
    public MyOrdersResponse() {}

    public List<OrderDto> Orders { get; set; } = new();
}

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IPaymentService paymentService) =>
            {
                return await HandleAsync(new MyOrdersRequest
                {
                    BuyerId = httpContext.User.Identity?.Name ?? string.Empty
                }, paymentService);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IPaymentService paymentService)
    {
        var orders = await paymentService.GetMyOrdersAsync(request.BuyerId);
        return Results.Ok(new MyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(OrderDtoMapper.ToDto).ToList()
        });
    }
}
