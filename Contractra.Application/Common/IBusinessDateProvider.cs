namespace Contractra.Application.Common;

/// <summary>
/// Today's date in the configured business time zone (<c>Business:TimeZone</c>,
/// default <c>Africa/Cairo</c>) - auth contract v1.5 §9.1, engineering decision E8.
/// </summary>
public interface IBusinessDateProvider
{
    DateOnly Today();
}
