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

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// List the caller's saved cards.
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedPaymentMethodService savedPaymentMethodService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var methods = await savedPaymentMethodService.ListAsync(buyerId, cancellationToken);

                var response = new ListPaymentMethodsResponse();
                response.PaymentMethods.AddRange(methods.Select(PaymentMethodDto.FromSavedPaymentMethod));
                return Results.Ok(response);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new List<PaymentMethodDto>();
}
