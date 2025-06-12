using DeviceConnector.Protos;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using SDK.Helper;
using Shared.Interface;
using Shared.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DeviceConnector.Services
{
    public class EmployeeService : Protos.EmployeeService.EmployeeServiceBase
    {
        private readonly SDKHelperManager _sdkHelperManager;
        private readonly INhanVienRepository _nhanVienRepository;

        public EmployeeService(SDKHelperManager sdkHelperManager, INhanVienRepository nhanVienRepository)
        {
            _sdkHelperManager = sdkHelperManager;
            _nhanVienRepository = nhanVienRepository;
        }

        private SDKHelper GetSDKHelper(ServerCallContext context)
        {
            // Get JWT token from authorization header
            var authHeader = context.GetHttpContext().Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "JWT token is required"));
            }

            // Extract the token (remove "Bearer " prefix)
            var token = authHeader.Substring(7);
            
            // Use the token as client ID
            return _sdkHelperManager.GetOrCreateSDKHelper(token);
        }

        #region Upload Employee Data
        public override async Task<BaseResponse> UploadEmployeeData(UploadEmployeeDataRequest request, ServerCallContext context)
        {
            if (request.Employee == null)
            {
                return new BaseResponse
                {
                    Success = false,
                    Message = "No employee data provided."
                };
            }

            var sdkHelper = GetSDKHelper(context);

            // Create Employee object from request
            var employee = new Employee
            {
                employeeId = request.Employee.EmployeeId,
                name = request.Employee.Name,
                password = request.Employee.Password,
                privilege = request.Employee.Privilege,
                enabled = request.Employee.Enable
            };

            bool success = await sdkHelper.SetUserAsync(employee);

            return new BaseResponse
            {
                Success = success,
                Message = success ? "Employee data uploaded successfully." : "Failed to upload employee data."
            };
        }

        public override async Task<BatchUploadResponse> BatchUploadEmployeeData(BatchUploadEmployeeDataRequest request, ServerCallContext context)
        {
            var response = new BatchUploadResponse();
            var sdkHelper = GetSDKHelper(context);

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
            bool success = await sdkHelper.BatchSetUserAsync(employees);

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
            response.Success = success;
            response.Message = success
                ? $"Successfully uploaded {results.Count} employees."
                : $"Failed to upload {results.Count} employees.";

            return response;
        }
        #endregion

        #region Get Employee Data

        public override async Task<GetAllEmployeesResponse> GetAllEmployees(Empty request, ServerCallContext context)
        {
            var response = new GetAllEmployeesResponse();
            var sdkHelper = GetSDKHelper(context);

            // Get employees from device
            var deviceEmployees = await sdkHelper.GetAllEmployeeAsync() ?? new List<Employee>();

            // Get employees from database
            var dbEmployees = await _nhanVienRepository.GetAllNhanVienAsync();

            if (dbEmployees == null)
            {
                response.Message = "Failed to retrieve employees from database.";
                response.Success = false;
                return response;
            }

            // Create lookup of device employee IDs for efficient comparison
            var deviceEmployeeIds = new HashSet<int>(deviceEmployees.Select(e => e.employeeId));

            // Filter database employees to only include those not on the device
            var employeesToAdd = dbEmployees.Where(dbEmp => !deviceEmployeeIds.Contains(dbEmp.MaNhanVien));

            if (!deviceEmployees.Any() && !employeesToAdd.Any())
            {
                response.Message = "No employees found in device or database.";
                response.Success = false;
                return response;
            }

            // Add filtered database employees to response
            foreach (var dbEmployee in employeesToAdd)
            {
                response.Employees.Add(new Protos.employee
                {
                    EmployeeId = dbEmployee.MaNhanVien,
                    Name = dbEmployee.HoTen,
                    Password = string.Empty,
                    Privilege = 0, 
                    Enable = dbEmployee.TrangThai == "Đang làm" ? true : false
                });
            }

            response.Success = true;
            response.Message = $"Employee list retrieved successfully. {deviceEmployees.Count} from device, {employeesToAdd.Count()} additional from database.";
            return response;
        }

        public override async Task<GetEmployeeDataResponse> GetEmployeeData(GetEmployeeDataRequest request, ServerCallContext context)
        {
            var sdkHelper = GetSDKHelper(context);
            Employee employee = await sdkHelper.GetUserAsync(request.EmployeeId);

            var response = new GetEmployeeDataResponse();

            if (employee == null)
            {
                response.Success = false;
                response.Message = "Employee not found.";
                return response;
            }

            response.Employee = new Protos.employee
            {
                EmployeeId = employee.employeeId,
                Name = employee.name,
                Password = employee.password,
                Privilege = employee.privilege,
                Enable = employee.enabled
            };

            response.Success = true;
            response.Message = "Employee data retrieved successfully.";

            return response;
        }
        #endregion

        #region Upload Fingerprint Data
        public override async Task<BaseResponse> UploadFingerprint(UploadFingerprintRequest request, ServerCallContext context)
        {
            if (request.Fingerprint == null)
            {
                return new BaseResponse
                {
                    Success = false,
                    Message = "No fingerprint data provided."
                };
            }

            var sdkHelper = GetSDKHelper(context);

            // Create Fingerprint object from request
            var fingerprint = new Fingerprint
            {
                employeeId = request.Fingerprint.EmployeeId,
                fingerIndex = request.Fingerprint.FingerIndex,
                fingerData = request.Fingerprint.FingerData,
                fingerLength = request.Fingerprint.FingerData.Length
            };

            bool success = await sdkHelper.SetFingerprintAsync(fingerprint);

            return new BaseResponse
            {
                Success = success,
                Message = success ? "Fingerprint uploaded successfully." : "Failed to upload fingerprint."
            };
        }

        public override async Task<BatchUploadResponse> BatchUploadFingerprints(BatchUploadFingerprintsRequest request, ServerCallContext context)
        {
            var response = new BatchUploadResponse();
            var sdkHelper = GetSDKHelper(context);

            if (request.Fingerprints.Count == 0)
            {
                response.Success = false;
                response.Message = "No fingerprints to upload.";
                return response;
            }

            // Convert request fingerprints to Fingerprint list
            var fingerprints = request.Fingerprints.Select(fpData => new Fingerprint
            {
                employeeId = fpData.EmployeeId,
                fingerIndex = fpData.FingerIndex,
                fingerData = fpData.FingerData,
                fingerLength = fpData.FingerData.Length
            }).ToList();

            // Use batch upload with explicit typing for the tuple
            (bool success, int successCount, int failureCount) = await sdkHelper.BatchSetFingerprintsAsync(fingerprints);

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
            response.Success = success;
            response.Message = success
                ? $"Successfully uploaded {successCount} fingerprints. Failed to upload {failureCount} fingerprints."
                : $"Failed to upload fingerprints. {failureCount} fingerprints failed.";

            return response;
        }
        #endregion

        #region Get Fingerprint Data 
        public override async Task<GetAllFingerprintsResponse> GetAllFingerprints(Empty request, ServerCallContext context)
        {
            var response = new GetAllFingerprintsResponse();
            var sdkHelper = GetSDKHelper(context);

            try
            {
                var result = await sdkHelper.GetAllFingerprintsAsync();

                response.TotalCount = result.TotalFound;
                response.SuccessCount = result.SavedCount;
                response.Success = result.Success;
                response.Message = result.Success
                    ? $"Successfully retrieved {result.TotalFound} fingerprints and saved {result.SavedCount}."
                    : "Failed to retrieve fingerprints from device.";

                return response;
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = $"Error retrieving fingerprints: {ex.Message}";
                response.TotalCount = 0;
                response.SuccessCount = 0;
                return response;
            }
        }

        public override async Task<BaseResponse> GetFingerprintData(GetFingerprintDataRequest request, ServerCallContext context)
        {
            var response = new BaseResponse();
            var sdkHelper = GetSDKHelper(context);

            try
            {
                // Get the specific fingerprint using GetFingerprintAsync instead
                var result = await sdkHelper.GetAllFingerprintsForEmployeeAsync(request.EmployeeId);

                response.Success = result.Success;
                response.Message = result.Success
                                   ? $"Successfully retrieved fingerprints and saved."
                                   : "Failed to retrieve fingerprints from device.";

                return response;
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = $"Error retrieving fingerprint: {ex.Message}";
                return response;
            }
        }
        #endregion

        #region Delete Employee
        public override async Task<BaseResponse> DeleteEmployeeData(DeleteEmployeeDataRequest request, ServerCallContext context)
        {
            var response = new BaseResponse();
            var sdkHelper = GetSDKHelper(context);

            if (request.EmployeeId <= 0)
            {
                response.Success = false;
                response.Message = "Invalid Employee ID.";
                return response;
            }

            bool success = await sdkHelper.DeleteUserAsync(request.EmployeeId);

            response.Success = success;
            response.Message = success
                ? "Employee deleted successfully."
                : "Failed to delete employee.";

            return response;
        }
        #endregion

        #region Delete Fingerprint
        public override async Task<BaseResponse> DeleteFingerprint(DeleteFingerprintRequest request, ServerCallContext context)
        {
            var response = new BaseResponse();
            var sdkHelper = GetSDKHelper(context);

            if (request.EmployeeId < 1 || request.FingerIndex < 0)
            {
                response.Success = false;
                response.Message = "Invalid Employee ID or Finger Index.";
                return response;
            }

            bool success = await sdkHelper.DeleteFingerprintAsync(request.EmployeeId, request.FingerIndex);

            response.Success = success;
            response.Message = success
                ? "Fingerprint deleted successfully."
                : "Failed to delete fingerprint.";

            return response;
        }
        #endregion

        public override async Task<BaseResponse> SyncAttendanceData(Empty request, ServerCallContext context)
        {
            var response = new BaseResponse();
            var sdkHelper = GetSDKHelper(context);

            try
            {
                bool success = await sdkHelper.SyncAttendanceAsync();

                response.Success = success;
                response.Message = success
                    ? "Attendance data synchronized successfully."
                    : "Failed to synchronize attendance data.";

                return response;
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = $"Error synchronizing attendance data: {ex.Message}";
                return response;
            }
        }
    }
}
