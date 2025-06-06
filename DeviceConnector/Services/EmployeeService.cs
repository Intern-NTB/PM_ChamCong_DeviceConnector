using DeviceConnector.Protos;
using Grpc.Core;
using DeviceConnector.Helper;
using DeviceConnector.Model;
using System.Threading.Tasks;

namespace DeviceConnector.Services
{
    public class EmployeeService : DeviceConnector.Protos.EmployeeService.EmployeeServiceBase
    {
        private readonly SDKHelper _sdkHelper;

        public EmployeeService(SDKHelper sdkHelper)
        {
            _sdkHelper = sdkHelper;
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
                Privilege = employee.privilege,
                Enable = employee.enabled
            };
            response.Message = "Employee data retrieved successfully.";

            return Task.FromResult(response);
        }

    }
}
