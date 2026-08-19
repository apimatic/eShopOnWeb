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
/// Registers a supplier: a name and the URL of its product-listing page. Operator-only.
/// </summary>
public class RegisterSupplierEndpoint : IEndpoint<IResult, RegisterSupplierRequest, ISupplierCatalogSyncService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterSupplierRequest request, ISupplierCatalogSyncService service) =>
            {
                return await HandleAsync(request, service);
            })
            .Produces<RegisterSupplierResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterSupplierRequest request, ISupplierCatalogSyncService service)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ListingUrl))
        {
            return Results.BadRequest("Both 'name' and 'listingUrl' are required.");
        }

        var supplier = await service.RegisterSupplierAsync(request.Name, request.ListingUrl);

        var response = new RegisterSupplierResponse(request.CorrelationId())
        {
            SupplierId = supplier.Id,
            Name = supplier.Name,
            ListingUrl = supplier.ProductListingUrl
        };

        return Results.Created($"api/catalog/suppliers/{supplier.Id}", response);
    }
}
