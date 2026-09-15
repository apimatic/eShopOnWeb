using System;
using System.Collections.Generic;
using System.Security.Claims;
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
    public string? BuyerId { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderSummary> Orders { get; set; } = new();
}

/// <summary>The caller's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = user.Identity?.Name }, service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var orders = await service.GetMyOrdersAsync(request.BuyerId);
        var response = new MyOrdersResponse(request.CorrelationId());
        response.Orders.AddRange(orders);
        return Results.Ok(response);
    }
}
