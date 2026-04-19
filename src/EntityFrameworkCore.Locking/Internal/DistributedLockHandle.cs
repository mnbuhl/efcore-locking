using System.Data.Common;
using EntityFrameworkCore.Locking.Abstractions;

namespace EntityFrameworkCore.Locking.Internal;

internal sealed class DistributedLockHandle : IDistributedLockHandle
{
    private readonly Func<CancellationToken, Task> _releaseAsync;
    private readonly Action _releaseSync;
    private int _released;

    public string Key { get; }
    public System.Data.Common.DbConnection Connection { get; }
    public bool OpenedByConnection { get; }

    public DistributedLockHandle(
        string key,
        DbConnection connection,
        bool openedByConnection,
        Func<CancellationToken, Task> releaseAsync,
        Action releaseSync)
    {
        Key = key;
        Connection = connection;
        OpenedByConnection = openedByConnection;
        _releaseAsync = releaseAsync;
        _releaseSync = releaseSync;
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
            return;
        await _releaseAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
            return;
        _releaseSync();
    }

    public ValueTask DisposeAsync() => new(ReleaseAsync(CancellationToken.None));

    public void Dispose() => Release();
}
