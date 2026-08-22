using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedPaymentMethodService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeletePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedPaymentMethodService methods) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId }, methods);
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedPaymentMethodService methods)
    {
        var buyerId = BuyerIdentity.Require(_httpContextAccessor);
        await methods.DeleteAsync(
            buyerId,
            request.PaymentMethodId,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);
        return Results.NoContent();
    }
}
