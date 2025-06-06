using DeviceConnector.Protos;
using Grpc.Core;
using DeviceConnector.Helper;
using DeviceConnector.Model;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;

namespace DeviceConnector.Services
{
    public class EmployeeService : DeviceConnector.Protos.EmployeeService.EmployeeServiceBase
    {
        private readonly SDKHelper _sdkHelper;

        public EmployeeService(SDKHelper sdkHelper)
        {
            _sdkHelper = sdkHelper;
        }

        public override Task<GetAllEmployeesResponse> GetAllEmployees(Empty request, ServerCallContext context)
        {
            var response = new GetAllEmployeesResponse();
            var employees = _sdkHelper.GetAllEmployee();
            if (employees == null || employees.Count == 0)
            {
                response.Message = "No employees found.";
                return Task.FromResult(response);
            }
            foreach (var employee in employees)
            {
                response.Employees.Add(new employee
                {
                    EmployeeId = employee.employeeId,
                    Name = employee.name,
                    Password = employee.password,
                    Privilege = employee.privilege,
                    Enable = employee.enabled
                });
            }
            response.Message = "Employee list retrieved successfully.";
            return Task.FromResult(response);
        }

        public override Task<GetEmployeeDataResponse> GetEmployeeData(GetEmployeeDataRequest request, ServerCallContext context)
        {
            Employee employee = _sdkHelper.GetUser(request.EmployeeId);

            var response = new GetEmployeeDataResponse();

            if (employee == null)
            {
                response.Message = "Employee not found.";
                return Task.FromResult(response);
            }

            response.Employee = new employee
            {
                EmployeeId = employee.employeeId,
                Name = employee.name,
                Password = employee.password,
                Privilege = employee.privilege,
                Enable = employee.enabled
            };
            response.Message = "Employee data retrieved successfully.";

            return Task.FromResult(response);
        }

        public override Task<UploadEmployeeDataResponse> UploadEmployeeData(UploadEmployeeDataRequest request, ServerCallContext context)
        {
            var employee = new Employee
            {
                employeeId = request.Employee.EmployeeId,
                name = request.Employee.Name,
                password = request.Employee.Password,
                privilege = request.Employee.Privilege,
                enabled = request.Employee.Enable
            };
            bool success = _sdkHelper.SetUser(employee);
            var response = new UploadEmployeeDataResponse
            {
                Success = success,
                Message = success ? "Employee data uploaded successfully." : "Failed to upload employee data."
            };
            return Task.FromResult(response);
        }
    }
}
