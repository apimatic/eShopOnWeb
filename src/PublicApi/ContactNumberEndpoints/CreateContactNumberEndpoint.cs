using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateContactNumberRequest request, HttpContext httpContext, IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(BindBuyer(request, httpContext), contactNumberService);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IContactNumberService contactNumberService)
    {
        var buyerId = request.BuyerId;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var created = await contactNumberService.RegisterAsync(buyerId, request.PhoneNumber, request.CountryCode);
        var response = new CreateContactNumberResponse
        {
            ContactNumberId = created.Id,
            PhoneNumber = created.PhoneNumber,
            NationalFormat = created.NationalFormat,
            CountryCode = created.CountryCode
        };

        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }

    private static CreateContactNumberRequest BindBuyer(CreateContactNumberRequest request, HttpContext httpContext)
    {
        request.BuyerId = httpContext.User.GetBuyerId();
        return request;
    }
}

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    internal string? BuyerId { get; set; }
}
