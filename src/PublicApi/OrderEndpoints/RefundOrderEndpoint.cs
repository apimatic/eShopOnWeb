using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, CreateRefundRequest, IPaidOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, CreateRefundRequest request, IPaidOrderService service, ClaimsPrincipal user) =>
                await HandleAsync(orderId, request, service, user))
            .Produces<CreateRefundResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateRefundRequest request, IPaidOrderService service) =>
        Task.FromResult(Results.BadRequest());

    private static async Task<IResult> HandleAsync(
        int orderId,
        CreateRefundRequest request,
        IPaidOrderService service,
        ClaimsPrincipal user)
    {
        var order = await service.RefundAsync(orderId, user.GetRequiredUserName(), request.IdempotencyKey, request.Amount);
        var refund = order.FindRefundByIdempotencyKey(request.IdempotencyKey)
            ?? throw new OrderPaymentException(500, "The refund was processed but could not be loaded.");

        return Results.Ok(new CreateRefundResponse
        {
            RefundId = refund.Id,
            PayPalRefundId = refund.PayPalRefundId,
            Status = refund.Status,
            Amount = refund.Amount,
            Order = OrderDtoMapper.ToDto(order)
        });
    }
}
