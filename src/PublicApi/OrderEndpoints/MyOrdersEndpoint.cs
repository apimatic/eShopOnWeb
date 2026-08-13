using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }

    public IReadOnlyList<OrderSummaryDto> Orders { get; set; } = new List<OrderSummaryDto>();
}

/// <summary>Returns the caller's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISmsNotificationService service) =>
                await HandleAsync(new MyOrdersRequest { BuyerId = user.GetBuyerId() }, service))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, ISmsNotificationService service)
    {
        var orders = await service.GetMyOrdersAsync(request.BuyerId);

        var summaries = new List<OrderSummaryDto>();
        foreach (var order in orders)
        {
            var notifications = await service.GetOrderNotificationsAsync(order.Id);
            var notificationSummaries = notifications.Select(NotificationSummary.From).ToList();
            summaries.Add(OrderSummaryDto.From(order, notificationSummaries));
        }

        var response = new MyOrdersResponse(request.CorrelationId()) { Orders = summaries };
        return Results.Ok(response);
    }
}
