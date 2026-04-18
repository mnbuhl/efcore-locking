namespace EntityFrameworkCore.Locking.Abstractions;

/// <summary>
/// Marker interface for Phase 2 advisory lock providers (pg_advisory_xact_lock, GET_LOCK, sp_getapplock).
/// Empty in Phase 1 — defined so ILockingProvider.AdvisoryLockProvider compiles.
/// Phase 2 adds members without changing the interface contract.
/// </summary>
public interface IAdvisoryLockProvider { }
