using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Lists the signed-in shopper's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint
    : IEndpoint<IResult, ListPaymentMethodsRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public ListPaymentMethodsEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest(), user, cancellationToken);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var saved = await _orderPaymentService.GetPaymentMethodsAsync(
            user.Identity!.Name!, cancellationToken);

        var response = new ListPaymentMethodsResponse(request.CorrelationId());
        response.PaymentMethods.AddRange(saved.Select(item => new PaymentMethodResponse
        {
            PaymentMethodId = item.PaymentMethodId,
            Brand = item.Brand,
            Last4 = item.Last4,
            ExpirationMonth = item.ExpirationMonth,
            ExpirationYear = item.ExpirationYear,
            Name = item.Name,
            CreatedAt = item.CreatedAt
        }));

        return Results.Ok(response);
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
}
