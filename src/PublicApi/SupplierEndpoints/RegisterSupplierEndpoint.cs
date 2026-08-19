using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Registers a supplier: a name and the URL of its product listing page. Operator-only.
/// </summary>
public class RegisterSupplierEndpoint : IEndpoint<IResult, RegisterSupplierRequest, ISupplierSyncService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterSupplierRequest request, ISupplierSyncService supplierSyncService) =>
            {
                return await HandleAsync(request, supplierSyncService);
            })
            .Produces<RegisterSupplierResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterSupplierRequest request, ISupplierSyncService supplierSyncService)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ProductListingUrl))
        {
            return Results.BadRequest("Both 'name' and 'productListingUrl' are required.");
        }

        if (!IsHttpUrl(request.ProductListingUrl))
        {
            return Results.BadRequest("'productListingUrl' must be a valid absolute http(s) URL.");
        }

        var supplier = await supplierSyncService.RegisterSupplierAsync(request.Name.Trim(), request.ProductListingUrl.Trim());

        var response = new RegisterSupplierResponse(request.CorrelationId())
        {
            SupplierId = supplier.Id,
            Name = supplier.Name,
            ProductListingUrl = supplier.ProductListingUrl
        };

        return Results.Created($"api/catalog/suppliers/{supplier.Id}", response);
    }

    private static bool IsHttpUrl(string url)
        => System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri) &&
           (uri.Scheme == System.Uri.UriSchemeHttp || uri.Scheme == System.Uri.UriSchemeHttps);
}
