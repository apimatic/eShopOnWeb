using System.Threading.Tasks;
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

public record CancelOrderRequest(int OrderId);

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>>
{
    private readonly IPayPalPaymentService _payPal;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CancelOrderEndpoint(IPayPalPaymentService payPal, IHttpContextAccessor httpContextAccessor)
    {
        _payPal = payPal;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepo) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), orderRepo);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> orderRepo)
    {
        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? default;
        var spec = new OrderWithPaymentByIdSpec(request.OrderId);
        var order = await orderRepo.FirstOrDefaultAsync(spec, ct);
        if (order == null) return Results.NotFound();
        if (order.PaymentStatus != PaymentStatus.Authorized)
            return Results.BadRequest($"Order is in state {order.PaymentStatus} and cannot be cancelled.");

        try
        {
            await _payPal.VoidAuthorizationAsync(order.AuthorizationId!, ct);
            order.Cancel();
            await orderRepo.UpdateAsync(order, ct);
            return Results.NoContent();
        }
        catch (PayPalPaymentException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: ex.StatusCode);
        }
    }
}
