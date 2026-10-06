using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Payments.Adyen;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersRequest : BaseRequest
{
    public MyOrdersRequest(string buyerId)
    {
        BuyerId = buyerId;
    }

    public string BuyerId { get; }
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
/// The calling shopper's orders with their payment state. Only ever the caller's own orders.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderPaymentStore>
{
    private readonly AdyenSettings _paymentSettings;

    public MyOrdersEndpoint(IOptions<AdyenSettings> paymentSettings)
    {
        _paymentSettings = paymentSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentStore store) =>
            {
                return await HandleAsync(new MyOrdersRequest(user.Identity!.Name!), store);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentStore store)
    {
        var response = new MyOrdersResponse(request.CorrelationId());
        var orders = await store.ListOrdersWithPaymentsForBuyerAsync(request.BuyerId, CancellationToken.None);
        response.Orders.AddRange(orders.Select(o => OrderSummaryDto.From(o, _paymentSettings.NormalizedCurrency)));
        return Results.Ok(response);
    }
}
