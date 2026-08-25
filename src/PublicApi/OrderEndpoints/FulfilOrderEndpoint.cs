using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IRepository<Order>>
{
    private readonly PayPalService _payPal;

    public FulfilOrderEndpoint(PayPalService payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   IRepository<Order> orderRepository,
                   CancellationToken ct) =>
            {
                var spec = new OrderByIdSpec(orderId);
                var order = await orderRepository.FirstOrDefaultAsync(spec, ct);

                if (order == null) return Results.NotFound();
                if (order.Status != OrderStatus.Authorized)
                    return Results.BadRequest($"Order is in status {order.Status} and cannot be fulfilled.");
                if (string.IsNullOrEmpty(order.PayPalAuthorizationId))
                    return Results.BadRequest("Order has no authorization ID.");

                var result = await _payPal.CaptureAsync(orderId, order.PayPalAuthorizationId, order.Total(), ct);

                if (!string.IsNullOrEmpty(result.NewAuthorizationId))
                    order.UpdateAuthorizationId(result.NewAuthorizationId);

                order.SetFulfilled(result.CaptureId, result.CapturedAmount, result.PayPalFee, result.NetAmount);
                await orderRepository.UpdateAsync(order, ct);

                return Results.Ok(new FulfilOrderResponse
                {
                    OrderId = order.Id,
                    CaptureId = result.CaptureId,
                    CapturedAmount = result.CapturedAmount,
                    PayPalFee = result.PayPalFee,
                    NetAmount = result.NetAmount,
                    Status = order.Status.ToString()
                });
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request, IRepository<Order> dep)
        => Task.FromResult(Results.StatusCode(501));
}

public class FulfilOrderRequest : BaseRequest { }

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse() : base(System.Guid.NewGuid()) { }
    public int OrderId { get; set; }
    public string CaptureId { get; set; } = string.Empty;
    public decimal CapturedAmount { get; set; }
    public decimal PayPalFee { get; set; }
    public decimal NetAmount { get; set; }
    public string Status { get; set; } = string.Empty;
}
