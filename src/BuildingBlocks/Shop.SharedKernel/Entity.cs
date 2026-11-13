namespace Shop.SharedKernel;

public abstract class Entity<TId> : IEquatable<Entity<TId>> where TId : notnull
{
    public TId Id { get; protected init; } = default!;

    public bool Equals(Entity<TId>? other) =>
        other is not null
        && GetType() == other.GetType()
        && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
