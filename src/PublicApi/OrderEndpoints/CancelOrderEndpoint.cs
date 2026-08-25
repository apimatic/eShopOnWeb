using System;
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

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>>
{
    private readonly IPayPalPaymentService _payPal;

    public CancelOrderEndpoint(IPayPalPaymentService payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepo, HttpContext ctx, CancellationToken ct) =>
            {
                var request = new CancelOrderRequest { OrderId = orderId };
                return await HandleAsync(request, orderRepo);
            })
            .Produces<CancelOrderResponse>()
            .Produces(404)
            .Produces(409)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> orderRepo)
    {
        var spec = new OrderWithItemsByIdSpec(request.OrderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec);

        if (order == null)
            return Results.NotFound();

        if (order.PaymentStatus != PaymentStatus.Authorized)
            return Results.Conflict($"Order cannot be cancelled in current state: {order.PaymentStatus}");

        var voidKey = $"cancel-{request.OrderId}";

        try
        {
            await _payPal.VoidAsync(order.AuthorizationId!, voidKey);
            order.RecordVoid();
            await orderRepo.UpdateAsync(order);

            return Results.Ok(new CancelOrderResponse(request.CorrelationId())
            {
                Status = order.PaymentStatus.ToString()
            });
        }
        catch (PayPalOperationException ex)
        {
            return Results.Problem(
                title: "Cancel error",
                detail: ex.Message,
                statusCode: (int)ex.StatusCode);
        }
    }
}

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public string Status { get; set; } = string.Empty;
}
