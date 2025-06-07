using DeviceConnector.Protos;
using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using SDK.Helper;
using Shared.Model;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DeviceConnector.Services
{
    public class EmployeeService : Protos.EmployeeService.EmployeeServiceBase
    {
        private readonly SDKHelper _sdkHelper;

        public EmployeeService(SDKHelper sdkHelper)
        {
            _sdkHelper = sdkHelper;
        }

        public override async Task<GetAllEmployeesResponse> GetAllEmployees(Empty request, ServerCallContext context)
        {
            var response = new GetAllEmployeesResponse();
            var employees = await _sdkHelper.GetAllEmployeeAsync();
            if (employees == null || employees.Count == 0)
            {
                response.Message = "No employees found.";
                return response;
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
            return response;
        }

        public override async Task<GetEmployeeDataResponse> GetEmployeeData(GetEmployeeDataRequest request, ServerCallContext context)
        {
            Employee employee = await _sdkHelper.GetUserAsync(request.EmployeeId);

            var response = new GetEmployeeDataResponse();

            if (employee == null)
            {
                response.Message = "Employee not found.";
                return response;
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

            return response;
        }

        public override async Task<UploadEmployeeDataResponse> UploadEmployeeData(UploadEmployeeDataRequest request, ServerCallContext context)
        {
            if (request.Employee == null)
            {
                return new UploadEmployeeDataResponse
                {
                    Success = false,
                    Message = "No employee data provided."
                };
            }

            // Create Employee object from request
            var employee = new Employee
            {
                employeeId = request.Employee.EmployeeId,
                name = request.Employee.Name,
                password = request.Employee.Password,
                privilege = request.Employee.Privilege,
                enabled = request.Employee.Enable
            };

            bool success = await _sdkHelper.SetUserAsync(employee);
            
            return new UploadEmployeeDataResponse
            {
                Success = success,
                Message = success ? "Employee data uploaded successfully." : "Failed to upload employee data."
            };
        }

        public override async Task<BatchUploadEmployeeDataResponse> BatchUploadEmployeeData(BatchUploadEmployeeDataRequest request, ServerCallContext context)
        {
            var response = new BatchUploadEmployeeDataResponse();
            
            if (request.Employees.Count == 0)
            {
                response.Message = "No employees to upload.";
                return response;
            }

            // Convert request employees to Employee list
            var employees = request.Employees.Select(empData => new Employee
            {
                employeeId = empData.EmployeeId,
                name = empData.Name,
                password = empData.Password,
                privilege = empData.Privilege,
                enabled = empData.Enable
            }).ToList();

            // Use batch upload
            bool success = await _sdkHelper.BatchSetUserAsync(employees);

            // Create results based on batch operation
            var results = employees.Select(emp => new UploadResult
            {
                EmployeeId = emp.employeeId,
                Success = success, // All succeed or all fail in batch operation
                Message = success ? "Success" : "Failed"
            }).ToList();

            // Add results to response
            response.Results.AddRange(results);
            response.SuccessCount = success ? results.Count : 0;
            response.FailureCount = success ? 0 : results.Count;
            response.Message = success 
                ? $"Successfully uploaded {results.Count} employees." 
                : $"Failed to upload {results.Count} employees.";
            
            return response;
        }
    }
}
