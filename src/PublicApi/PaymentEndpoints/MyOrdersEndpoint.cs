using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class MyOrdersRequest : BaseRequest { }

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public IReadOnlyList<OrderPaymentSummary> Orders { get; set; } = new List<OrderPaymentSummary>();
}

/// <summary>Lists the signed-in shopper's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var orders = await service.GetOrdersForBuyerAsync(buyerId, ct);
                return Results.Ok(new MyOrdersResponse(Guid.NewGuid()) { Orders = orders });
            })
            .Produces<MyOrdersResponse>()
            .WithTags("Orders");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentService service) =>
        Task.FromResult(Results.Empty as IResult);
}
