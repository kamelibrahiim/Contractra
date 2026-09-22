using Contractra.Domain.Identity;

namespace Contractra.Domain.Tests.Identity;

public class PermissionCodeTests
{
    [Fact]
    public void Constructor_Throws_ForNull()
    {
        Assert.Throws<ArgumentException>(() => new PermissionCode(null!));
    }

    [Fact]
    public void Constructor_Throws_ForEmpty()
    {
        Assert.Throws<ArgumentException>(() => new PermissionCode(string.Empty));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Constructor_Throws_ForWhitespaceOnly(string value)
    {
        Assert.Throws<ArgumentException>(() => new PermissionCode(value));
    }

    [Theory]
    [InlineData(" Payroll.View")]
    [InlineData("Payroll.View ")]
    [InlineData("Users.Manage\t")]
    public void Constructor_Throws_ForLeadingOrTrailingWhitespace(string value)
    {
        Assert.Throws<ArgumentException>(() => new PermissionCode(value));
    }

    // No segment-count or pattern rule: the confirmed codes differ in shape.
    [Theory]
    [InlineData("Payroll.View")]
    [InlineData("Reports.Monthly.View")]
    [InlineData("Users.Manage")]
    [InlineData("Expenses.ApproveAboveLimit")]
    public void Constructor_AcceptsCodesOfAnyShape_AndPreservesCasing(string value)
    {
        var code = new PermissionCode(value);

        Assert.Equal(value, code.Value);
    }

    [Theory]
    [InlineData("Payroll.View")]
    [InlineData("payroll.view")]
    [InlineData("PAYROLL.VIEW")]
    [InlineData("PayRoll.vIEW")]
    public void IsPayrollView_IsCaseInsensitive(string value)
    {
        Assert.True(new PermissionCode(value).IsPayrollView);
    }

    [Theory]
    [InlineData("Payroll.Approve")]
    [InlineData("Payroll.View.Extra")]
    [InlineData("PayrollView")]
    [InlineData("Reports.Monthly.View")]
    public void IsPayrollView_IsFalse_ForOtherCodes(string value)
    {
        Assert.False(new PermissionCode(value).IsPayrollView);
    }
}
