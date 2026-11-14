namespace Catalog.Domain.Categories;

public abstract record SpecValue;

public sealed record NumberValue(decimal Value) : SpecValue;

public sealed record TextValue(string Value) : SpecValue;

public sealed record BoolValue(bool Value) : SpecValue;
