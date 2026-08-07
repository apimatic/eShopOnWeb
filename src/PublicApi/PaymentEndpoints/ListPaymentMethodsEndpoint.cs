using System.Collections.Generic;
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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

/// <summary>Lists the calling shopper's saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedPaymentMethodService service, CancellationToken ct) =>
            {
                return await HandleAsync(service, user.GetBuyerId(), ct);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    // Required by IEndpoint; the routed lambda calls the buyer-scoped overload instead.
    public Task<IResult> HandleAsync(ISavedPaymentMethodService service)
        => HandleAsync(service, string.Empty, default);

    public async Task<IResult> HandleAsync(ISavedPaymentMethodService service, string buyerId, CancellationToken ct)
    {
        var response = new ListPaymentMethodsResponse();

        if (!string.IsNullOrEmpty(buyerId))
        {
            var methods = await service.ListAsync(buyerId, ct);
            response.PaymentMethods = methods.Select(PaymentMethodDto.From).ToList();
        }

        return Results.Ok(response);
    }
}
