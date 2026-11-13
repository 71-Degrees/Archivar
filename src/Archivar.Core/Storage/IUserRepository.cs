/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Archivar.Core.Accounts;

namespace Archivar.Core.Storage;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Overwrites an existing account (same email); a no-op if it does not exist.</summary>
    Task SaveAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>Moves the account to <paramref name="newEmail"/> and saves it. Throws <see cref="InvalidOperationException"/>
    /// if another account already uses that address.</summary>
    Task ChangeEmailAsync(User user, string newEmail, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="InvalidOperationException"/> if the email is already registered.</summary>
    Task<User> CreateAsync(User user, CancellationToken cancellationToken = default);
}
