namespace Contractra.Domain.Parties;

public sealed class PartyRole
{
    public Guid Id { get; private set; }

    public Guid PartyId { get; private set; }

    public PartyRoleCode RoleCode { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    public DateOnly? EffectiveTo { get; private set; }

    private PartyRole()
    {
    }

    public PartyRole(Guid partyId, PartyRoleCode roleCode, DateOnly effectiveFrom, DateOnly? effectiveTo = null)
        : this(Guid.CreateVersion7(), partyId, roleCode, effectiveFrom, effectiveTo)
    {
    }

    public PartyRole(Guid id, Guid partyId, PartyRoleCode roleCode, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (effectiveTo is not null && effectiveTo < effectiveFrom)
        {
            throw new ArgumentException("EffectiveTo must be greater than or equal to EffectiveFrom.", nameof(effectiveTo));
        }

        Id = id;
        PartyId = partyId;
        RoleCode = roleCode;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    /// <summary>
    /// Inclusive on both ends: EffectiveTo == asOf is active, EffectiveTo == null is open-ended.
    /// </summary>
    public bool IsActiveOn(DateOnly asOf) =>
        asOf >= EffectiveFrom && (EffectiveTo is null || asOf <= EffectiveTo);
}
