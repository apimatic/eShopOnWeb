using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentStateDto Payment { get; set; } = new();
}

/// <summary>
/// Operator action: marks the order fulfilled and captures the held funds.
/// A stale authorization is renewed automatically when PayPal still allows it.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest>
{
    private readonly IPaymentService _paymentService;

    public FulfilOrderEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId });
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request)
    {
        try
        {
            var order = await _paymentService.FulfilOrderAsync(request.OrderId);
            var dto = OrderDto.FromOrder(order);
            return Results.Ok(new FulfilOrderResponse(request.CorrelationId())
            {
                OrderId = dto.OrderId,
                Status = dto.Status,
                Payment = dto.Payment
            });
        }
        catch (Exception ex) when (PaymentEndpointHelpers.TryMapException(ex) is { } result)
        {
            return result;
        }
    }
}
