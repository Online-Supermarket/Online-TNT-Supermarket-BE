using System.ComponentModel.DataAnnotations;

namespace TNT.ProductService.Api.DTOs;

public sealed class NotBlankAttribute : ValidationAttribute
{
    public NotBlankAttribute()
    {
        ErrorMessage = "The {0} field is required.";
    }

    public override bool IsValid(object? value)
    {
        return value is string text && !string.IsNullOrWhiteSpace(text);
    }
}
