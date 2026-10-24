using Contractra.Infrastructure.Persistence.Throttling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Contractra.Infrastructure.Persistence.Configurations;

/// <summary>
/// auth_throttle_counters (Backend Sprint 2, Slice 2B; auth contract v1.5 §17;
/// ADR 0005 §3.4).
/// </summary>
public sealed class AuthThrottleCounterConfiguration : IEntityTypeConfiguration<AuthThrottleCounter>
{
    public void Configure(EntityTypeBuilder<AuthThrottleCounter> builder)
    {
        builder.ToTable("auth_throttle_counters", t =>
        {
            t.HasCheckConstraint(
                "ck_auth_throttle_counters_scope_valid_values",
                $"scope IN ({string.Join(", ", AuthThrottleScopes.All.Select(scope => $"'{scope}'"))})");
            t.HasCheckConstraint("ck_auth_throttle_counters_key_hash_length", "octet_length(key_hash) = 32");

            // A row exists only while it holds at least one counted attempt.
            t.HasCheckConstraint("ck_auth_throttle_counters_failure_count_positive", "failure_count >= 1");
            t.HasCheckConstraint(
                "ck_auth_throttle_counters_blocked_until_not_before_window_start",
                "blocked_until IS NULL OR blocked_until >= window_start");
        });

        builder.HasKey(c => new { c.Scope, c.KeyHash }).HasName("pk_auth_throttle_counters");

        builder.Property(c => c.Scope)
            .HasColumnName("scope")
            .IsRequired();

        builder.Property(c => c.KeyHash)
            .HasColumnName("key_hash")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(c => c.WindowStart)
            .HasColumnName("window_start")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(c => c.FailureCount)
            .HasColumnName("failure_count")
            .IsRequired();

        builder.Property(c => c.BlockedUntil)
            .HasColumnName("blocked_until")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(c => c.WindowStart)
            .HasDatabaseName("ix_auth_throttle_counters_window_start");
    }
}
