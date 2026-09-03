using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
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
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CaptureId { get; set; } = string.Empty;
    public string CaptureStatus { get; set; } = string.Empty;
    public decimal CapturedAmount { get; set; }
    public decimal? PaypalFee { get; set; }
    public decimal? NetAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
}

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, ICheckoutService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ICheckoutService checkout, CancellationToken ct) =>
            {
                var result = await checkout.FulfilAsync(orderId, ct);
                return Results.Ok(new FulfilOrderResponse
                {
                    OrderId = result.OrderId,
                    Status = result.Status.ToString(),
                    CaptureId = result.CaptureId,
                    CaptureStatus = result.CaptureStatus,
                    CapturedAmount = result.CapturedAmount,
                    PaypalFee = result.PaypalFee,
                    NetAmount = result.NetAmount,
                    Currency = result.Currency
                });
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request, ICheckoutService checkout) =>
        Task.FromResult(Results.BadRequest());
}
