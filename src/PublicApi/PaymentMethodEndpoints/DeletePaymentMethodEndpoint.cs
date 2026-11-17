using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    [JsonIgnore]
    public int PaymentMethodId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) {}
    public DeletePaymentMethodResponse() {}

    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext httpContext, IPaymentService paymentService) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = httpContext.User.Identity?.Name ?? string.Empty
                }, paymentService);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentService paymentService)
    {
        await paymentService.DeletePaymentMethodAsync(request.BuyerId, request.PaymentMethodId);

        return Results.Ok(new DeletePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = request.PaymentMethodId,
            Deleted = true
        });
    }
}
