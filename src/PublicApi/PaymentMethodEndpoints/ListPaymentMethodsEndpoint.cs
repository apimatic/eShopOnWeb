using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsRequest : BaseRequest
{
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ISavedPaymentMethodService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListPaymentMethodsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISavedPaymentMethodService methods) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest(), methods);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ISavedPaymentMethodService methods)
    {
        var buyerId = BuyerIdentity.Require(_httpContextAccessor);
        var result = await methods.ListAsync(buyerId, _httpContextAccessor.HttpContext?.RequestAborted ?? default);
        return Results.Ok(new ListPaymentMethodsResponse(request.CorrelationId())
        {
            PaymentMethods = result.Select(m => PaymentMethodResponse.From(m, request.CorrelationId())).ToList()
        });
    }
}
