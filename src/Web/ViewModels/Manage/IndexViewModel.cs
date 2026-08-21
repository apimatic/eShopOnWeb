using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Web.ViewModels.Manage;

public class IndexViewModel
{
    [StringLength(100)]
    [Display(Name = "First name")]
    public string? FirstName { get; set; }

    [StringLength(100)]
    [Display(Name = "Last name")]
    public string? LastName { get; set; }

    public string? Username { get; set; }

    public bool IsEmailConfirmed { get; set; }

    [Required]
    [EmailAddress]
    public string? Email { get; set; }

    [Phone]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    public string? StatusMessage { get; set; }
}
