using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Identity.Infrastructure.Persistence;

internal sealed class UserRepository(IdentityDbContext db) : IUserRepository
{
    private const string EmailIndex = "ix_users_email_normalized";

    private IQueryable<User> Users => db.Users.Include(u => u.RefreshTokens);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Users.SingleOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByNormalizedEmailAsync(string normalized, CancellationToken ct) =>
        Users.SingleOrDefaultAsync(u => u.Email.Normalized == normalized, ct);

    public Task<User?> GetByRefreshTokenHashAsync(string tokenHash, CancellationToken ct) =>
        Users.SingleOrDefaultAsync(u => u.RefreshTokens.Any(t => t.TokenHash == tokenHash), ct);

    public void Add(User user) => db.Users.Add(user);

    /// <summary>Saves, translating EF/Npgsql failures into exceptions the application layer understands.</summary>
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                                           && pg.ConstraintName == EmailIndex)
        {
            db.ChangeTracker.Clear();
            throw new DuplicateEmailException(ex);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Forget the stale entities so the caller's reload reads the current rows.
            db.ChangeTracker.Clear();
            throw new ConcurrencyConflictException(ex);
        }
    }
}
