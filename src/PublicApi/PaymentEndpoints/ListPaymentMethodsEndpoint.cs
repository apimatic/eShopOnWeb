using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, PaymentService>
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
            (PaymentService paymentService) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest(), paymentService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, PaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name
            ?? throw new PaymentValidationException("The caller identity is missing from the token.");

        var response = await paymentService.ListPaymentMethodsAsync(
            buyerId,
            request,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);

        return Results.Ok(response);
    }
}
