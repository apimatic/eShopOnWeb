using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: marks the order fulfilled and captures the held funds. The response
/// shows what PayPal reported: captured amount, PayPal's fee and the net proceeds.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromRoute] int orderId, IPaymentService paymentService) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, paymentService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IPaymentService paymentService)
    {
        var payment = await paymentService.FulfilOrderAsync(request.OrderId);

        var response = new FulfilOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Status = "Fulfilled",
            Payment = PaymentDto.FromEntity(payment)
        };
        return Results.Ok(response);
    }
}

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
}
