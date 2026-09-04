using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Shared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Lists the caller's orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new MyOrdersRequest(), paymentService);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentService paymentService)
    {
        var response = new MyOrdersResponse(request.CorrelationId());
        var buyerId = CallerIdentity.Get(_httpContextAccessor.HttpContext);
        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? System.Threading.CancellationToken.None;

        var orders = await paymentService.GetMyOrdersAsync(buyerId, ct);

        foreach (var order in orders)
        {
            var dto = new OrderSummaryDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Currency = order.Currency,
                PayPalOrderId = order.PayPalOrderId,
                AuthorizationStatus = order.AuthorizationStatus,
                CaptureId = order.CaptureId,
                CaptureStatus = order.CaptureStatus,
                RefundedAmount = order.RefundedAmount
            };

            foreach (var item in order.OrderItems)
            {
                dto.Items.Add(new OrderItemDto
                {
                    CatalogItemId = item.ItemOrdered.CatalogItemId,
                    ProductName = item.ItemOrdered.ProductName,
                    UnitPrice = item.UnitPrice,
                    Units = item.Units
                });
            }

            response.Orders.Add(dto);
        }

        return Results.Ok(response);
    }
}