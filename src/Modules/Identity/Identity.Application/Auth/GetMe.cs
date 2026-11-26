using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Shop.Application.Abstractions.Messaging;
using Shop.SharedKernel;

namespace Identity.Application.Auth;

public sealed record GetMe(Guid UserId) : IQuery<UserDto>;

internal sealed class GetMeHandler(IUserRepository users) : IQueryHandler<GetMe, UserDto>
{
    public async Task<Result<UserDto>> Handle(GetMe query, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(query.UserId, ct);
        return user is null ? IdentityErrors.UserNotFound() : UserDto.From(user);
    }
}
