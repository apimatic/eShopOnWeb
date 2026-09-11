namespace Microsoft.eShopWeb.ApplicationCore.Services.Maxio;

public class MaxioPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string FamilyHandle { get; set; } = string.Empty;
}
