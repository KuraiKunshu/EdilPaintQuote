using EdilPaintPreventibiviGen.Views;
using Xunit;

namespace EdilPaintPreventibiviGen.Tests;

public sealed class WorkSheetEmployeeSearchTests
{
    [Fact]
    public void SearchSupportsSurnameFirstAccentsAndMultipleTerms()
    {
        var employee = new WorkSheetEmployeeSelection { DisplayName = "Niccolò Rossi", IsSelected = true };
        Assert.True(WorkSheetOptionsWindow.EmployeeMatches(employee, " ROSSI niccolo ", false));
        Assert.False(WorkSheetOptionsWindow.EmployeeMatches(employee, "Mario Rossi", false));
        Assert.True(employee.IsSelected);
    }

    [Fact]
    public void FilteringOneHundredEmployeesKeepsSelectionsOutsideResults()
    {
        var employees = Enumerable.Range(1, 100).Select(i => new WorkSheetEmployeeSelection
        {
            DisplayName = $"Dipendente {i:000}", IsSelected = i == 1 || i == 100
        }).ToArray();
        Assert.Single(employees, e => WorkSheetOptionsWindow.EmployeeMatches(e, "100", false));
        Assert.Equal(2, employees.Count(e => WorkSheetOptionsWindow.EmployeeMatches(e, "", true)));
        Assert.Equal(100, employees.Count(e => WorkSheetOptionsWindow.EmployeeMatches(e, "", false)));
        employees[0].IsSelected = false;
        Assert.Single(employees, e => WorkSheetOptionsWindow.EmployeeMatches(e, "", true));
    }
}
