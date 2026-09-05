using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>The caller's own orders, with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IPaymentService>
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
            (IPaymentService paymentService) =>
            {
                return await HandleAsync(new MyOrdersRequest(), paymentService);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(_httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal());
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await paymentService.GetMyOrdersAsync(buyerId, default);

        var response = new MyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.OrderId,
                OrderDate = o.OrderDate,
                Status = o.Status,
                Total = o.Total,
                Currency = o.Currency,
                Items = o.Items.Select(i => new OrderItemViewDto
                {
                    CatalogItemId = i.CatalogItemId,
                    Name = i.Name,
                    PictureUri = i.PictureUri,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Quantity
                }).ToList(),
                Payment = o.Payment == null ? null : new PaymentStateDto
                {
                    PaymentId = o.Payment.PaymentId,
                    State = o.Payment.State,
                    Amount = o.Payment.Amount,
                    Currency = o.Payment.Currency,
                    PayPalOrderId = o.Payment.PayPalOrderId,
                    AuthorizationId = o.Payment.AuthorizationId,
                    AuthorizationStatus = o.Payment.AuthorizationStatus,
                    AuthorizationExpiresAt = o.Payment.AuthorizationExpiresAt,
                    CaptureId = o.Payment.CaptureId,
                    CaptureStatus = o.Payment.CaptureStatus,
                    CapturedAmount = o.Payment.CapturedAmount,
                    PayPalFee = o.Payment.PayPalFee,
                    NetAmount = o.Payment.NetAmount,
                    CapturedAt = o.Payment.CapturedAt,
                    RefundedAmount = o.Payment.RefundedAmount,
                    Refunds = o.Payment.Refunds.Select(r => new PaymentRefundDto
                    {
                        RefundId = r.RefundId,
                        PayPalRefundId = r.PayPalRefundId,
                        Status = r.Status,
                        Amount = r.Amount,
                        Currency = r.Currency,
                        CreatedAt = r.CreatedAt
                    }).ToList()
                }
            }).ToList()
        };

        return Results.Ok(response);
    }
}

public class MyOrdersRequest : BaseRequest
{
}



