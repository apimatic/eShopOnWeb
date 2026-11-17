using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: refunds a captured payment in full or in part. Repeating the same
/// idempotencyKey does not refund twice.
/// </summary>
public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefundOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/refunds",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (int orderId, RefundOrderRequest request, IPaymentService paymentService) =>
                {
                    return await HandleAsync(request, paymentService);
                })
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IPaymentService paymentService)
    {
        var orderId = PaymentEndpointHelpers.RequireOrderId(_httpContextAccessor);

        long amountCents;
        if (request.AmountCents.HasValue && request.Amount.HasValue)
        {
            var fromAmount = PaymentDtoMapper.ToCents(request.Amount.Value);
            if (fromAmount != request.AmountCents.Value)
            {
                throw new PaymentValidationException("amountCents and amount describe different refund amounts.");
            }

            amountCents = request.AmountCents.Value;
        }
        else if (request.AmountCents.HasValue)
        {
            amountCents = request.AmountCents.Value;
        }
        else if (request.Amount.HasValue)
        {
            amountCents = PaymentDtoMapper.ToCents(request.Amount.Value);
        }
        else
        {
            throw new PaymentValidationException("Provide amountCents or amount for the refund.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentValidationException("idempotencyKey is required for refunds.");
        }

        var result = await paymentService.RefundOrderAsync(
            orderId,
            amountCents,
            request.IdempotencyKey.Trim(),
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);

        return Results.Ok(new RefundOrderResponse
        {
            RefundId = result.Refund.Id,
            FinixRefundId = result.Refund.FinixTransferId,
            OrderId = orderId,
            AmountCents = result.Refund.AmountCents,
            State = result.Refund.State,
            Payment = PaymentDtoMapper.ToResponse(result.Payment)
        });
    }
}
