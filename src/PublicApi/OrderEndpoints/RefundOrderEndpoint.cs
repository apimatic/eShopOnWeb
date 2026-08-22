using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderApiRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderApiRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service, user);
            })
            .Produces<CreateRefundResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(RefundOrderApiRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(RefundOrderApiRequest request, IOrderPaymentService service, ClaimsPrincipal user)
    {
        var buyerId = EndpointUser.RequireBuyerId(user);
        var refund = await service.RefundAsync(
            buyerId,
            request.OrderId,
            new RefundOrderRequest
            {
                IdempotencyKey = request.IdempotencyKey,
                Amount = request.Amount
            },
            EndpointUser.IsAdministrator(user));

        var dto = RefundDto.From(refund);
        return Results.Created($"api/orders/{request.OrderId}/refunds/{dto.RefundId}", new CreateRefundResponse
        {
            RefundId = dto.RefundId,
            Refund = dto
        });
    }
}

public class RefundOrderApiRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class CreateRefundResponse
{
    public int RefundId { get; set; }
    public RefundDto Refund { get; set; } = new();
}
