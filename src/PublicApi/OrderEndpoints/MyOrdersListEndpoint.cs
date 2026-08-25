using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersListRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class MyOrdersListResponse : BaseResponse
{
    public MyOrdersListResponse(Guid correlationId) : base(correlationId) { }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>
/// The signed-in shopper's own orders, including their payment state.
/// </summary>
public class MyOrdersListEndpoint : IEndpoint<IResult, MyOrdersListRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IOrderPaymentService paymentService) =>
            {
                var request = new MyOrdersListRequest { BuyerId = httpContext.User.Identity!.Name! };
                return await HandleAsync(request, paymentService);
            })
            .Produces<MyOrdersListResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersListRequest request, IOrderPaymentService paymentService)
    {
        var response = new MyOrdersListResponse(request.CorrelationId());

        var orders = await paymentService.GetOrdersForBuyerAsync(request.BuyerId);

        response.Orders = orders.Select(order => new OrderSummaryDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            OrderDate = order.OrderDate,
            Payment = order.Payment is null ? null : new PaymentSummaryDto
            {
                AuthorizationId = order.Payment.PayPalAuthorizationId,
                AuthorizationStatus = order.Payment.AuthorizationStatus,
                AuthorizedAmount = order.Payment.AuthorizedAmount,
                CaptureId = order.Payment.PayPalCaptureId,
                CaptureStatus = order.Payment.CaptureStatus,
                CapturedAmount = order.Payment.CapturedAmount,
                PayPalFee = order.Payment.PayPalFeeAmount,
                NetAmount = order.Payment.NetAmount,
                TotalRefunded = order.Payment.TotalRefunded,
                Currency = order.Payment.CurrencyCode
            }
        }).ToList();

        return Results.Ok(response);
    }
}
