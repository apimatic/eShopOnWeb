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

    public IReadOnlyList<OrderSummary> Orders { get; set; } = new List<OrderSummary>();
}

/// <summary>The caller's own orders, each showing where its notifications got to.</summary>
public class GetMyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderQueryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderQueryService service, ClaimsPrincipal user) =>
            {
                var request = new MyOrdersRequest { BuyerId = CallerIdentity.GetUserName(user) };
                return await HandleAsync(request, service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderQueryService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await service.GetMyOrdersAsync(request.BuyerId);
        return Results.Ok(new MyOrdersResponse(request.CorrelationId()) { Orders = orders });
    }
}
