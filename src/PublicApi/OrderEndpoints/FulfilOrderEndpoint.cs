using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using BlazorShared.Authorization;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator flow: mark the order fulfilled and capture (take) the authorized money.
/// A stale authorization is renewed first; the response shows the captured amount,
/// PayPal's fee and the net proceeds to the merchant.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentProcessingService paymentProcessing) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), paymentProcessing);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IPaymentProcessingService paymentProcessing)
    {
        var response = new FulfilOrderResponse(request.CorrelationId());

        var order = await paymentProcessing.FulfilOrderAsync(request.OrderId);

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.Payment = order.Payment is null ? null : PaymentStateDto.From(order.Payment);
        return Results.Ok(response);
    }
}

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; init; }

    public FulfilOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public FulfilOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentStateDto? Payment { get; set; }
}
