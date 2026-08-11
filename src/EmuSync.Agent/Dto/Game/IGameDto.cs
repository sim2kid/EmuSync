using FluentValidation;
using EmuSync.Domain.Helpers;

namespace EmuSync.Agent.Dto.Game;

/// <summary>
/// Common properties across the create and update DTOs to allow shared validation logic
/// </summary>
public interface IGameDto
{
    string Name { get; set; }
    bool AutoSync { get; set; }
    Dictionary<string, string>? SyncSourceIdLocations { get; set; }
    Dictionary<string, List<GamePathEntryDto>>? SyncSourceIdLocationsV2 { get; set; }
    int? MaximumLocalGameBackups { get; set; }
}

/// <summary>
/// Shared validation logic between the Create and Update DTO
/// </summary>
public class GameDtoValidator : AbstractValidator<IGameDto>
{
    public GameDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.MaximumLocalGameBackups).GreaterThan(-1).When(x => x.MaximumLocalGameBackups != null);
        RuleForEach(x => x.SyncSourceIdLocationsV2!.SelectMany(y => y.Value))
            .SetValidator(new GamePathEntryDtoValidator())
            .When(x => x.SyncSourceIdLocationsV2 != null);
    }
}

public class GamePathEntryDtoValidator : AbstractValidator<GamePathEntryDto>
{
    public GamePathEntryDtoValidator()
    {
        RuleFor(x => x.Path).NotEmpty();
        RuleForEach(x => x.IncludeFilters).Must(BeValidGlob).WithMessage("'{PropertyValue}' is not a valid glob pattern.");
        RuleForEach(x => x.ExcludeFilters).Must(BeValidGlob).WithMessage("'{PropertyValue}' is not a valid glob pattern.");
    }

    private static bool BeValidGlob(string pattern)
    {
        try
        {
            PathFilter.ValidatePattern(pattern);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
