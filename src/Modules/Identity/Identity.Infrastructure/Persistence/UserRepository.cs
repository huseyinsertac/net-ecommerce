using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence;

internal sealed class UserRepository(IdentityDbContext db) : IUserRepository
{
    private IQueryable<User> Users => db.Users.Include(u => u.RefreshTokens);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Users.SingleOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByNormalizedEmailAsync(string normalized, CancellationToken ct) =>
        Users.SingleOrDefaultAsync(u => u.Email.Normalized == normalized, ct);

    public Task<User?> GetByRefreshTokenHashAsync(string tokenHash, CancellationToken ct) =>
        Users.SingleOrDefaultAsync(u => u.RefreshTokens.Any(t => t.TokenHash == tokenHash), ct);

    public void Add(User user) => db.Users.Add(user);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
