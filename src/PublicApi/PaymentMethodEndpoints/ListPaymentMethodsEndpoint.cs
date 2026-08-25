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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IReadRepository<SavedPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IReadRepository<SavedPaymentMethod> pmRepo,
                   ClaimsPrincipal user,
                   CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var spec = new SavedPaymentMethodsByBuyerSpec(buyerId);
                var methods = await pmRepo.ListAsync(spec, ct);

                var dtos = methods.Select(m => new PaymentMethodDto
                {
                    PaymentMethodId = m.Id,
                    Last4 = m.Last4,
                    CardBrand = m.CardBrand,
                    CreatedAt = m.CreatedAt
                }).ToList();

                return Results.Ok(new ListPaymentMethodsResponse { PaymentMethods = dtos });
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IReadRepository<SavedPaymentMethod> service)
        => throw new System.NotSupportedException();
}

public class ListPaymentMethodsRequest : BaseRequest { }

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Last4 { get; set; }
    public string? CardBrand { get; set; }
    public System.DateTimeOffset CreatedAt { get; set; }
}
