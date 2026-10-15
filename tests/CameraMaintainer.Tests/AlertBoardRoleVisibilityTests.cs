using CameraMaintainer.Core.Models;

namespace CameraMaintainer.Tests;

public sealed class AlertBoardRoleVisibilityTests
{
    [Fact]
    public void Normalize_KeepsOnlyKnownRolesAndAlwaysIncludesAdministrator()
    {
        var result = AlertBoardRoleVisibility.Normalize(["technician", "Unknown", "Technician"]);

        Assert.Equal(["Admin", "Technician"], result);
    }

    [Fact]
    public void Normalize_MissingLegacyValueKeepsPreviousVisibility()
    {
        var result = AlertBoardRoleVisibility.Normalize(null);

        Assert.Equal(AlertBoardRoleVisibility.All(), result);
    }

    [Fact]
    public void IsVisibleTo_UsesRoleListButAdministratorAlwaysSeesColumn()
    {
        var column = new AlertBoardColumn
        {
            Key = "assigned",
            Title = "Назначено технику",
            VisibleToRoles = ["Admin", "Technician"]
        };

        Assert.True(AlertBoardRoleVisibility.IsVisibleTo(column, "Technician", false));
        Assert.False(AlertBoardRoleVisibility.IsVisibleTo(column, "Operator", false));
        Assert.True(AlertBoardRoleVisibility.IsVisibleTo(column, "Admin", true));
    }

    [Fact]
    public void IsVisibleTo_IntersectsRoleAndIndividualAccountAccess()
    {
        var column = new AlertBoardColumn
        {
            Key = "working",
            Title = "В работе",
            VisibleToRoles = ["Admin", "Operator", "Technician"]
        };

        Assert.True(AlertBoardRoleVisibility.IsVisibleTo(column, "Technician", false, null));
        Assert.True(AlertBoardRoleVisibility.IsVisibleTo(column, "Technician", false, "assigned,working"));
        Assert.False(AlertBoardRoleVisibility.IsVisibleTo(column, "Technician", false, "assigned,review"));
        Assert.False(AlertBoardRoleVisibility.IsVisibleTo(column, "Viewer", false, "working"));
        Assert.True(AlertBoardRoleVisibility.IsVisibleTo(column, "Admin", true, string.Empty));
    }

    [Fact]
    public void AccountColumnKeys_EmptySelectionIsDifferentFromInheritedAccess()
    {
        Assert.Null(AlertBoardRoleVisibility.SerializeAccountColumnKeys(null));
        Assert.Equal(string.Empty, AlertBoardRoleVisibility.SerializeAccountColumnKeys([]));
        Assert.Equal("new,working", AlertBoardRoleVisibility.SerializeAccountColumnKeys([" new ", "working", "NEW"]));
    }

}
