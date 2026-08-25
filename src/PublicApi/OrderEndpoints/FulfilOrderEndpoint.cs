using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IRepository<Order>>
{
    private readonly IPayPalPaymentService _payPal;

    public FulfilOrderEndpoint(IPayPalPaymentService payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepo, HttpContext ctx, CancellationToken ct) =>
            {
                var request = new FulfilOrderRequest { OrderId = orderId };
                return await HandleAsync(request, orderRepo);
            })
            .Produces<FulfilOrderResponse>()
            .Produces(404)
            .Produces(409)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IRepository<Order> orderRepo)
    {
        var spec = new OrderWithItemsByIdSpec(request.OrderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec);

        if (order == null)
            return Results.NotFound();

        if (order.PaymentStatus != PaymentStatus.Authorized)
            return Results.Conflict($"Order cannot be fulfilled in current state: {order.PaymentStatus}");

        var authId = order.AuthorizationId!;
        var captureKey = $"fulfil-{request.OrderId}";

        // Renew stale authorization if needed
        var renewResult = await _payPal.RenewAuthorizationIfNeededAsync(
            authorizationId: authId,
            amount: order.Total(),
            currency: string.Empty,
            idempotencyKey: $"reauth-{request.OrderId}");

        if (renewResult.Renewed)
        {
            order.UpdateAuthorizationId(renewResult.AuthorizationId);
            authId = renewResult.AuthorizationId;
        }
        else if (!renewResult.Renewed && renewResult.OperatorMessage != null)
        {
            return Results.Problem(
                title: "Authorization cannot be renewed",
                detail: renewResult.OperatorMessage,
                statusCode: 409);
        }

        try
        {
            var capture = await _payPal.CaptureWithBreakdownAsync(
                authorizationId: authId,
                idempotencyKey: captureKey);

            order.RecordCapture(capture.CaptureId, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
            await orderRepo.UpdateAsync(order);

            return Results.Ok(new FulfilOrderResponse(request.CorrelationId())
            {
                CaptureId = capture.CaptureId,
                CapturedAmount = capture.CapturedAmount,
                PayPalFee = capture.PayPalFee,
                NetAmount = capture.NetAmount,
                Status = order.PaymentStatus.ToString()
            });
        }
        catch (PayPalOperationException ex) when (ex.IsOperatorActionable)
        {
            return Results.Problem(
                title: "Fulfilment error",
                detail: ex.Message,
                statusCode: 409);
        }
        catch (PayPalOperationException ex)
        {
            return Results.Problem(
                title: "Payment capture error",
                detail: ex.Message,
                statusCode: (int)ex.StatusCode);
        }
    }
}

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }
    public string? CaptureId { get; set; }
    public decimal CapturedAmount { get; set; }
    public decimal? PayPalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Status { get; set; } = string.Empty;
}
