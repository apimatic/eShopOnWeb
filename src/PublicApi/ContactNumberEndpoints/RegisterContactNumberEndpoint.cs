using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (RegisterContactNumberRequest request, IContactNumberService service, HttpContext httpContext) =>
            {
                return await HandleAsync(request, service, httpContext);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
        => HandleAsync(request, service, new DefaultHttpContext());

    private async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service, HttpContext httpContext)
    {
        try
        {
            var buyerId = httpContext.GetRequiredBuyerId();
            var created = await service.RegisterAsync(buyerId, request.PhoneNumber, request.CountryCode);
            var response = new RegisterContactNumberResponse
            {
                ContactNumberId = created.Id,
                PhoneNumber = created.PhoneNumber,
                NationalFormat = created.NationalFormat,
                CountryCode = created.CountryCode,
                LineType = created.LineType
            };
            return Results.Created($"api/contact-numbers/{created.Id}", response);
        }
        catch (ContactNumberRejectedException ex)
        {
            return Results.BadRequest(new
            {
                message = ex.Message,
                validationErrors = ex.ValidationErrors,
                lineType = ex.LineType
            });
        }
    }
}
