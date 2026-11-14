namespace Catalog.Domain.Categories;

public enum SpecType { Number, Text, Boolean, Enum }

public sealed record SpecDefinition(
    string Key,
    string Label,
    SpecType Type,
    string? Unit,
    bool Filterable,
    IReadOnlyList<string> Options);

public abstract record SpecValue;

public sealed record NumberValue(decimal Value) : SpecValue;

public sealed record TextValue(string Value) : SpecValue;

public sealed record BoolValue(bool Value) : SpecValue;
