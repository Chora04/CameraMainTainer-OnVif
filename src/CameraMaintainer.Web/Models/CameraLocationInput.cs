using System.ComponentModel.DataAnnotations;

namespace CameraMaintainer.Web.Models;

public sealed record CameraLocationInput : IValidatableObject
{
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    [StringLength(600)]
    public string? StreetAddress { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Latitude.HasValue != Longitude.HasValue)
            yield return new ValidationResult("Укажите широту и долготу вместе.", [nameof(Latitude), nameof(Longitude)]);
        if (Latitude.HasValue && (!double.IsFinite(Latitude.Value) || Latitude < -90 || Latitude > 90))
            yield return new ValidationResult("Широта должна быть от -90 до 90.", [nameof(Latitude)]);
        if (Longitude.HasValue && (!double.IsFinite(Longitude.Value) || Longitude < -180 || Longitude > 180))
            yield return new ValidationResult("Долгота должна быть от -180 до 180.", [nameof(Longitude)]);
    }
}
