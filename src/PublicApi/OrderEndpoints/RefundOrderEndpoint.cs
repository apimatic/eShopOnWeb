using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    public decimal? Amount { get; set; }
    public decimal? RefundAmount { get; set; }
    public int? AmountCents { get; set; }
    public int? RefundAmountCents { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? IdempotencyId { get; set; }
}

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RefundOrderRequest request, IPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        var amountCents = request.AmountCents
                          ?? request.RefundAmountCents
                          ?? PaymentApiMapping.ToCents(request.Amount ?? request.RefundAmount);
        var idempotencyKey = request.IdempotencyKey ?? request.IdempotencyId;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException(400, "An idempotencyKey is required for refunds.");
        }

        var refund = await paymentService.RefundOrderAsync(
            request.OrderId,
            amountCents,
            idempotencyKey!,
            CancellationToken.None);

        return Results.Ok(refund.ToRefundDto());
    }
}
