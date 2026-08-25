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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IRepository<SavedPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<SavedPaymentMethod> repository,
                   ClaimsPrincipal user,
                   CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var spec = new SavedPaymentMethodsByBuyerSpec(buyerId);
                var methods = await repository.ListAsync(spec, ct);

                var dtos = methods.Select(m => new PaymentMethodDto
                {
                    PaymentMethodId = m.Id,
                    LastDigits = m.LastDigits,
                    Brand = m.Brand,
                    Expiry = m.Expiry
                }).ToList();

                return Results.Ok(new ListPaymentMethodsResponse { PaymentMethods = dtos });
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IRepository<SavedPaymentMethod> dep)
        => Task.FromResult(Results.StatusCode(501));
}

public class ListPaymentMethodsRequest : BaseRequest { }

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse() : base(System.Guid.NewGuid()) { }
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string LastDigits { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string? Expiry { get; set; }
}
