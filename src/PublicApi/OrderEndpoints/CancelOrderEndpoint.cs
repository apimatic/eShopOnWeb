using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(System.Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public bool AlreadyCancelled { get; set; }
    public PaymentDto? Payment { get; set; }
}

/// <summary>
/// Operator action: cancels the order before fulfilment, releasing the shopper's held funds.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public CancelOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId });
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request)
    {
        var result = await _orderPaymentService.CancelAsync(request.OrderId);

        var response = new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = result.Order.Id,
            OrderStatus = result.Order.Status.ToString(),
            AlreadyCancelled = result.AlreadyCancelled,
            Payment = result.Payment is null ? null : PaymentDto.FromEntity(result.Payment)
        };
        return Results.Ok(response);
    }
}
