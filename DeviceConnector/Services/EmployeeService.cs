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

        // Add new method for batch uploads
        public override async Task<BatchUploadEmployeeDataResponse> BatchUploadEmployeeData(BatchUploadEmployeeDataRequest request, ServerCallContext context)
        {
            var response = new BatchUploadEmployeeDataResponse();
            var results = new List<UploadResult>();
            
            if (request.Employees.Count == 0)
            {
                response.Message = "No employees to upload.";
                return response;
            }

            // Process employees concurrently
            var tasks = request.Employees.Select(async empData => 
            {
                var employee = new Employee
                {
                    employeeId = empData.EmployeeId,
                    name = empData.Name,
                    password = empData.Password,
                    privilege = empData.Privilege,
                    enabled = empData.Enable
                };
                
                bool success = await _sdkHelper.SetUserAsync(employee);
                
                return new UploadResult 
                { 
                    EmployeeId = employee.employeeId,
                    Success = success,
                    Message = success ? "Success" : "Failed"
                };
            }).ToList();
            
            // Wait for all uploads to complete
            var uploadResults = await Task.WhenAll(tasks);
            
            // Add results to response
            response.Results.AddRange(uploadResults);
            response.SuccessCount = uploadResults.Count(r => r.Success);
            response.FailureCount = uploadResults.Length - response.SuccessCount;
            response.Message = $"Uploaded {response.SuccessCount} of {uploadResults.Length} employees.";
            
            return response;
        }
    }
}
