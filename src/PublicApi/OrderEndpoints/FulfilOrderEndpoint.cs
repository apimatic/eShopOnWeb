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

public class FulfilOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(System.Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public bool AlreadyFulfilled { get; set; }
    public bool AuthorizationRenewed { get; set; }
    public PaymentDto Payment { get; set; } = new();
}

/// <summary>
/// Operator action: marks the order fulfilled and captures the held funds.
/// A stale authorization is renewed first; the response reports PayPal's captured
/// amount, fee and net proceeds.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public FulfilOrderEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId });
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request)
    {
        var result = await _orderPaymentService.FulfilAsync(request.OrderId);

        var response = new FulfilOrderResponse(request.CorrelationId())
        {
            OrderId = result.Order.Id,
            OrderStatus = result.Order.Status.ToString(),
            AlreadyFulfilled = result.AlreadyFulfilled,
            AuthorizationRenewed = result.AuthorizationRenewed,
            Payment = PaymentDto.FromEntity(result.Payment)
        };
        return Results.Ok(response);
    }
}
