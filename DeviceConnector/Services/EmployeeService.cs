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
        public override async Task<DeleteEmployeeDataResponse> DeleteEmployeeData(DeleteEmployeeDataRequest request, ServerCallContext context)
        {
            var response = new DeleteEmployeeDataResponse();
            if (request.EmployeeId <= 0)
            {
                response.Message = "Invalid Employee ID.";
                return response;
            }
            bool success = await _sdkHelper.DeleteUserAsync(request.EmployeeId);
            response.Success = success;
            response.Message = success 
                ? "Employee deleted successfully." 
                : "Failed to delete employee.";
            return response;
        }
        public override async Task<GetAllFingerprintsResponse> GetAllFingerprints(Empty request, ServerCallContext context)
        {
            var response = new GetAllFingerprintsResponse();

            try
            {
                var result = await _sdkHelper.GetAllFingerprintsAsync();

                response.TotalCount = result.TotalFound;
                response.SuccessCount = result.SavedCount;
                response.Message = result.Success
                    ? $"Successfully retrieved {result.TotalFound} fingerprints and saved {result.SavedCount}."
                    : "Failed to retrieve fingerprints from device.";

                return response;
            }
            catch (Exception ex)
            {
                response.Message = $"Error retrieving fingerprints: {ex.Message}";
                response.TotalCount = 0;
                response.SuccessCount = 0;
                return response;
            }
        }
        public override async Task<BatchUploadFingerprintsResponse> BatchUploadFingerprints(BatchUploadFingerprintsRequest request, ServerCallContext context)
        {
            var response = new BatchUploadFingerprintsResponse();
            
            if (request.Fingerprints.Count == 0)
            {
                response.Message = "No fingerprints to upload.";
                return response;
            }

            // Convert request fingerprints to Fingerprint list
            var fingerprints = request.Fingerprints.Select(fpData => new Fingerprint
            {
                employeeId = fpData.EmployeeId,
                fingerIndex = fpData.FingerIndex,
                fingerData = fpData.FingerData,
            }).ToList();

            // Use batch upload
            var (success, successCount, failureCount) = await _sdkHelper.BatchSetFingerprintsAsync(fingerprints);

            // Create results based on batch operation
            var results = fingerprints.Select(fp => new UploadResult
            {
                EmployeeId = fp.employeeId,
                Success = success && successCount > 0, // Only consider successful if batch succeeded and at least one fingerprint was added
                Message = success ? "Success" : "Failed"
            }).ToList();

            // Add results to response
            response.Results.AddRange(results);
            response.SuccessCount = successCount;
            response.FailureCount = failureCount;
            response.Message = success 
                ? $"Successfully uploaded {successCount} fingerprints. Failed to upload {failureCount} fingerprints." 
                : $"Failed to upload fingerprints. {failureCount} fingerprints failed.";
            
            return response;
        }
    }
}
