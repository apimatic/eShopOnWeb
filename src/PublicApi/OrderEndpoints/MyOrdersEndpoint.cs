using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
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

    [JsonIgnore]
    public CancellationToken CancellationToken { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MyOrdersResponse()
    {
    }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>
/// The caller's own orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var request = new MyOrdersRequest
                {
                    BuyerId = OrderEndpointUser.RequireName(user),
                    CancellationToken = cancellationToken
                };
                return await HandleAsync(request, paymentService);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentService paymentService)
    {
        var response = new MyOrdersResponse(request.CorrelationId());
        var orders = await paymentService.GetOrdersForBuyerAsync(request.BuyerId, request.CancellationToken);
        response.Orders = orders.Select(o => OrderSummaryDto.From(o, paymentService.Currency)).ToList();
        return Results.Ok(response);
    }
}
