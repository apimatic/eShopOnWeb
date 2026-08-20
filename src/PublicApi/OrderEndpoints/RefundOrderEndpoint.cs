using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, RefundOrderRequest request, ClaimsPrincipal user, HttpRequest httpRequest, IOrderPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                request.BuyerId = user.GetBuyerId();
                request.IsAdministrator = user.IsAdministrator();
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey) &&
                    httpRequest.Headers.TryGetValue("Idempotency-Key", out var headerKey))
                {
                    request.IdempotencyKey = headerKey.ToString();
                }

                return await HandleAsync(request, paymentService);
            })
            .Produces<RefundOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(RefundOrderRequest request, IOrderPaymentService paymentService)
    {
        var refund = await paymentService.RefundAsync(
            request.OrderId,
            request.BuyerId,
            request.IsAdministrator,
            request.Amount,
            request.IdempotencyKey);

        var response = new RefundOrderResponse(request.CorrelationId())
        {
            RefundId = refund.Id,
            Refund = PaymentDtoFactory.From(refund)
        };
        return Results.Created($"api/orders/{request.OrderId}/refunds/{refund.Id}", response);
    }
}

public class RefundOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsAdministrator { get; set; }

    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }

    public RefundOrderResponse() { }

    public int RefundId { get; set; }
    public RefundDto Refund { get; set; } = new();
}
