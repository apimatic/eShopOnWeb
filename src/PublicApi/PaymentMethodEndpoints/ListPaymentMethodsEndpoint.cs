using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Shared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Lists the caller's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IOrderPaymentService>
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
            (IOrderPaymentService paymentService) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest(), paymentService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IOrderPaymentService paymentService)
    {
        var response = new ListPaymentMethodsResponse(request.CorrelationId());
        var buyerId = CallerIdentity.Get(_httpContextAccessor.HttpContext);
        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? System.Threading.CancellationToken.None;

        var paymentMethods = await paymentService.GetPaymentMethodsAsync(buyerId, ct);

        foreach (var paymentMethod in paymentMethods)
        {
            response.PaymentMethods.Add(new PaymentMethodDto
            {
                PaymentMethodId = paymentMethod.Id,
                Brand = paymentMethod.Brand,
                Last4 = paymentMethod.Last4,
                Expiry = paymentMethod.Expiry,
                CreatedAt = paymentMethod.CreatedAt
            });
        }

        return Results.Ok(response);
    }
}