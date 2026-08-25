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
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   IRepository<Order> orderRepo,
                   PayPalPaymentService paypal,
                   CancellationToken ct) =>
            {
                var spec = new OrderByIdSpec(orderId);
                var order = await orderRepo.FirstOrDefaultAsync(spec, ct);
                if (order == null) return Results.NotFound();
                if (order.Status != OrderStatus.PaymentAuthorized)
                    return Results.Conflict(new { error = $"Order is in status {order.Status}, cannot fulfil." });

                var authId = order.Payment!.AuthorizationId!;

                CaptureResult captureResult;
                try
                {
                    captureResult = await paypal.CaptureAsync(authId, order.Id, ct);
                }
                catch (PayPalAuthorizationExpiredException)
                {
                    // Try to reauthorize
                    ReauthorizeResult reauth;
                    try
                    {
                        reauth = await paypal.ReauthorizeAsync(authId, order.Total(), order.Id, ct);
                    }
                    catch (PayPalException reauthEx)
                    {
                        return Results.Problem(
                            detail: $"Authorization expired and renewal failed: {reauthEx.Message}",
                            statusCode: 422);
                    }

                    order.UpdateAuthorizationId(reauth.NewAuthorizationId);
                    await orderRepo.UpdateAsync(order, ct);

                    try
                    {
                        captureResult = await paypal.CaptureAsync(reauth.NewAuthorizationId, order.Id, ct);
                    }
                    catch (PayPalException ex)
                    {
                        return Results.Problem(detail: ex.Message, statusCode: ex.HttpStatusCode);
                    }
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: ex.HttpStatusCode);
                }

                order.Fulfil(
                    captureResult.CaptureId,
                    captureResult.CapturedAmount,
                    captureResult.Currency,
                    captureResult.FeeAmount,
                    captureResult.NetAmount);
                await orderRepo.UpdateAsync(order, ct);

                return Results.Ok(new FulfilOrderResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString(),
                    CaptureId = captureResult.CaptureId,
                    CapturedAmount = captureResult.CapturedAmount,
                    Currency = captureResult.Currency,
                    PayPalFee = captureResult.FeeAmount,
                    NetAmount = captureResult.NetAmount
                });
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request, IRepository<Order> service)
        => throw new System.NotSupportedException();
}

public class FulfilOrderRequest : BaseRequest { }

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public string? CapturedAmount { get; set; }
    public string? Currency { get; set; }
    public string? PayPalFee { get; set; }
    public string? NetAmount { get; set; }
}
