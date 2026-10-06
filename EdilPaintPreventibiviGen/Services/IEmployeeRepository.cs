using EdilPaintPreventibiviGen.Models;

namespace EdilPaintPreventibiviGen.Services;

public interface IEmployeeRepository
{
    Task<List<EmployeeSettingsModel>> GetEmployeesAsync(CancellationToken token = default);
    Task ImportLegacyEmployeesAsync(IReadOnlyList<EmployeeSettingsModel> employees, CancellationToken token = default);
    Task SaveEmployeesAsync(IReadOnlyList<EmployeeSettingsModel> original,
        IReadOnlyList<EmployeeSettingsModel> edited, CancellationToken token = default);
}
