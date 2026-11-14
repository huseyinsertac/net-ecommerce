namespace Catalog.Domain.Categories;

public sealed record SpecDefinition(
    string Key,
    string Label,
    SpecType Type,
    string? Unit,
    bool Filterable,
    IReadOnlyList<string> Options);
