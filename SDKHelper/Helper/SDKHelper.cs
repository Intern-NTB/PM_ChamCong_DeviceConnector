using System;
using System.Collections.Generic;
using zkemkeeper;
using Shared.Model;
using Shared.Entity;
using Shared.Interface;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;

namespace SDK.Helper
{
    public class SDKHelper
    {
        private static readonly SemaphoreSlim _deviceLock = new SemaphoreSlim(1, 1);
        private static CZKEM connector = new CZKEM();
        private static bool _isConnected = false;
        private static string _deviceSerial = string.Empty;
        private static int _deviceNumber = 1;
        private readonly INhanVienRepository _nhanVienRepository;
        private readonly IChamCongRepository _chamCongRepository;
        private readonly ILogger<SDKHelper> _logger;

        public SDKHelper(INhanVienRepository nhanVienRepository, IChamCongRepository chamCongRepository, ILogger<SDKHelper> logger)
        {
            _nhanVienRepository = nhanVienRepository;
            _chamCongRepository = chamCongRepository;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Connect/Disconnect Methods
        public bool GetConnectionStatus()
        {
            _logger.LogDebug("Getting connection status: {Status}", _isConnected);
            return _isConnected;
        }

        public void SetConnectionStatus(bool isConnected)
        {
            _logger.LogInformation("Setting connection status to {Status}", isConnected);
            _isConnected = isConnected;
        }

        public int GetDeviceNumber()
        {
            _logger.LogDebug("Getting device number: {DeviceNumber}", _deviceNumber);
            return _deviceNumber;
        }

        public void SetDeviceNumber(int deviceNumber)
        {
            _logger.LogInformation("Setting device number to {DeviceNumber}", deviceNumber);
            _deviceNumber = deviceNumber;
        }

        public async Task<string> GetDeviceSerialAsync()
        {
            try
            {
                if (!GetConnectionStatus())
                {
                    _logger.LogWarning("Attempted to get device serial while not connected");
                    return string.Empty;
                }
                
                return await Task.Run(() =>
                {
                    if (connector.GetSerialNumber(GetDeviceNumber(), out _deviceSerial))
                    {
                        _logger.LogInformation("Retrieved device serial: {Serial}", _deviceSerial);
                        return _deviceSerial;
                    }
                    else
                    {
                        _logger.LogWarning("Failed to get device serial number");
                        return string.Empty;
                    }
                });
            }
            catch (Exception ex)
            {
                int errorCode = 0;
                connector.GetLastError(ref errorCode);
                _logger.LogError(ex, "Error getting device serial number ", errorCode);
                return string.Empty;
            }
        }

        public void SetDeviceSerial(string deviceSerial)
        {
            _logger.LogInformation("Setting device serial to {Serial}", deviceSerial);
            _deviceSerial = deviceSerial;
        }

        public async Task<bool> EnableDeviceAsync()
        {
            // --- BƯỚC 0: KIỂM TRA KẾT NỐI ---
            _logger.LogInformation("Starting device enable process");
            if (!GetConnectionStatus())
            {
                _logger.LogError("Cannot enable device: Not connected to the device.");
                return false;
            }

            // Sử dụng lock để đảm bảo không có thao tác nào khác xen vào
            await _deviceLock.WaitAsync();
            try
            {
                bool finalResult = false;
                try
                {
                    // --- BƯỚC 1: THỰC HIỆN ENABLE DEVICE ---
                    finalResult = await Task.Run(() => connector.EnableDevice(_deviceNumber, true));
                    if (finalResult)
                    {
                        _logger.LogInformation("Successfully enabled device");
                    }
                    else
                    {
                        int errorCode = 0;
                        connector.GetLastError(ref errorCode);
                        _logger.LogWarning("Failed to enable device. Error: {ErrorCode}", errorCode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during device enable process");
                    return false;
                }
                finally
                {
                    // --- BƯỚC 2: DỌN DẸP ---
                    // Không cần disable ở đây vì đây là hàm enable
                    _logger.LogInformation("Device enable operation completed.");
                }

                return finalResult;
            }
            finally
            {
                _deviceLock.Release();
            }
        }

        public async Task<bool> DisableDeviceAsync()
        {
            // --- BƯỚC 0: KIỂM TRA KẾT NỐI ---
            _logger.LogInformation("Starting device disable process");
            if (!GetConnectionStatus())
            {
                _logger.LogError("Cannot disable device: Not connected to the device.");
                return false;
            }

            // Sử dụng lock để đảm bảo không có thao tác nào khác xen vào
            await _deviceLock.WaitAsync();
            try
            {
                bool finalResult = false;
                try
                {
                    // --- BƯỚC 1: THỰC HIỆN DISABLE DEVICE ---
                    finalResult = await Task.Run(() => connector.EnableDevice(_deviceNumber, false));
                    if (finalResult)
                    {
                        _logger.LogInformation("Successfully disabled device");
                    }
                    else
                    {
                        int errorCode = 0;
                        connector.GetLastError(ref errorCode);
                        _logger.LogWarning("Failed to disable device. Error: {ErrorCode}", errorCode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during device disable process");
                    return false;
                }
                finally
                {
                    // --- BƯỚC 2: DỌN DẸP ---
                    // Không cần disable thêm vì đây là hàm disable
                    _logger.LogInformation("Device disable operation completed.");
                }

                return finalResult;
            }
            finally
            {
                _deviceLock.Release();
            }
        }

        public async Task DisconnectAsync()
        {
            // --- BƯỚC 0: KIỂM TRA TRẠNG THÁI HIỆN TẠI ---
            _logger.LogInformation("Starting disconnection process");
            if (!_isConnected)
            {
                _logger.LogDebug("Disconnect called but already disconnected");
                return;
            }

            // Sử dụng lock để đảm bảo không có thao tác nào khác xen vào
            await _deviceLock.WaitAsync();
            try
            {
                try
                {
                    // --- BƯỚC 1: DISABLE DEVICE TRƯỚC KHI NGẮT KẾT NỐI ---
                    await Task.Run(() => connector.EnableDevice(_deviceNumber, false));
                    _logger.LogInformation("Device disabled successfully");

                    // --- BƯỚC 2: NGẮT KẾT NỐI ---
                    await Task.Run(() => connector.Disconnect());
                    SetConnectionStatus(false);
                    SetDeviceSerial(string.Empty);
                    _logger.LogInformation("Successfully disconnected from device");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during disconnection process");
                    throw;
                }
                finally
                {
                    // --- BƯỚC 3: DỌN DẸP ---
                    // Không cần disable thêm vì đã disable ở bước 1
                    _logger.LogInformation("Disconnection process completed.");
                }
            }
            finally
            {
                _deviceLock.Release();
            }
        }

        public async Task<bool> ConnectAsync(string ipAddress, int port)
        {
            // --- BƯỚC 0: KIỂM TRA TRẠNG THÁI HIỆN TẠI ---
            _logger.LogInformation("Starting connection process to device at {IpAddress}:{Port}", ipAddress, port);
            if (_isConnected)
            {
                _logger.LogWarning("Already connected to a device");
                return true;
            }

            // Sử dụng lock để đảm bảo không có thao tác nào khác xen vào
            await _deviceLock.WaitAsync();
            try
            {
                bool finalResult = false;
                try
                {
                    // --- BƯỚC 1: THỰC HIỆN KẾT NỐI ---
                    bool connectResult = await Task.Run(() => connector.Connect_Net(ipAddress, port));
                    if (!connectResult)
                    {
                        _logger.LogWarning("Failed to connect to device at {IpAddress}:{Port}", ipAddress, port);
                        return false;
                    }

                    SetConnectionStatus(true);
                    _logger.LogInformation("Successfully connected to device at {IpAddress}:{Port}", ipAddress, port);

                    // --- BƯỚC 2: ENABLE DEVICE SAU KHI KẾT NỐI ---
                    if (!await Task.Run(() => connector.EnableDevice(_deviceNumber, true)))
                    {
                        _logger.LogWarning("Failed to enable device after connection");
                        await DisconnectAsync(); // Clean up connection
                        return false;
                    }
                    _logger.LogInformation("Device enabled successfully");

                    // --- BƯỚC 3: ĐĂNG KÝ SỰ KIỆN REALTIME ---
                    if (!await Task.Run(() => RegisterRealtimeEventAsync()))
                    {
                        _logger.LogWarning("Failed to register realtime events after connection");
                        await DisconnectAsync(); // Clean up connection
                        return false;
                    }
                    _logger.LogInformation("Realtime events registered successfully");

                    finalResult = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during connection process to {IpAddress}:{Port}", ipAddress, port);
                    await DisconnectAsync(); // Clean up connection
                    return false;
                }
                finally
                {
                    // --- BƯỚC 4: DỌN DẸP ---
                    // Không cần disable ở đây vì đây là hàm connect
                    _logger.LogInformation("Connection process completed.");
                }

                return finalResult;
            }
            finally
            {
                _deviceLock.Release();
            }
        }
        #endregion

        #region User Management Methods
            #region User Information Methods
            public async Task<List<Employee>> GetAllEmployeeAsync()
            {
                try
                {
                    _logger.LogInformation("Getting all employees from device");
                    if (!GetConnectionStatus())
                    {
                        var ex = new InvalidOperationException("Not connected to the device.");
                        _logger.LogError(ex, "Failed to get employees: not connected");
                        throw ex;
                    }

                    var employees = new List<Employee>();

                    bool readResult = await Task.Run(() => connector.ReadAllUserID(_deviceNumber));
                    if (readResult)
                    {
                        string dwEnrollNumber = "";
                        string Name = string.Empty;
                        string Password = string.Empty;
                        int Privilege = 0;
                        bool Enabled = false;

                        // Move to the first user
                        bool hasUser = await Task.Run(() => connector.SSR_GetAllUserInfo(_deviceNumber, out dwEnrollNumber, out Name, out Password, out Privilege, out Enabled));
                        while (hasUser)
                        {
                            var employee = new Employee
                            {
                                employeeId = Int32.Parse(dwEnrollNumber),
                                name = Name,
                                password = Password,
                                privilege = Privilege,
                                enabled = Enabled
                            };
                            employees.Add(employee);
                            _logger.LogDebug("Found employee: ID={ID}, Name={Name}", dwEnrollNumber, Name);

                            // Move to the next user
                            hasUser = await Task.Run(() => connector.SSR_GetAllUserInfo(_deviceNumber, out dwEnrollNumber, out Name, out Password, out Privilege, out Enabled));
                        }
                    }
                    else
                    {
                        _logger.LogWarning("ReadAllUserID returned false");
                    }
                    
                    _logger.LogInformation("Retrieved {Count} employees from device", employees.Count);
                    return employees;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving employees from device");
                    throw;
                }
            }

            public async Task<Employee> GetUserAsync(int employeeId)
            {
                try
                {
                    _logger.LogInformation("Getting employee with ID: {EmployeeId}", employeeId);
                    if (!GetConnectionStatus())
                    {
                        var ex = new InvalidOperationException("Not connected to the device.");
                        _logger.LogError(ex, "Failed to get employee: not connected");
                        throw ex;
                    }
                    
                    Employee employee = new Employee();
                    string Name = string.Empty;
                    string Password = string.Empty;
                    int Privilege = 0;
                    bool Enabled = false;
                    
                    bool result = await Task.Run(() => connector.SSR_GetUserInfo(_deviceNumber, employeeId.ToString(), out Name, out Password, out Privilege, out Enabled));
                    if (result)
                    {
                        employee.employeeId = employeeId;
                        employee.name = Name;
                        employee.password = Password;
                        employee.privilege = Privilege;
                        employee.enabled = Enabled;
                        _logger.LogInformation("Successfully retrieved employee with ID: {EmployeeId}, Name: {Name}", employeeId, Name);
                    }
                    else
                    {
                        _logger.LogWarning("Employee with ID {EmployeeId} not found", employeeId);
                    }
                    
                    return employee;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving employee with ID: {EmployeeId}", employeeId);
                    throw;
                }
            }

            public async Task<bool> SetUserAsync(Employee employee)
            {
                // --- BƯỚC 0: KIỂM TRA KẾT NỐI ---
                _logger.LogInformation("Starting set user process for employee ID: {EmployeeId}, Name: {Name}", 
                    employee.employeeId, employee.name);
                    
                if (!GetConnectionStatus())
                {
                    _logger.LogError("Cannot set user: Not connected to the device.");
                    return false;
                }

                // Sử dụng lock để đảm bảo không có thao tác nào khác xen vào
                await _deviceLock.WaitAsync();
                try
                {
                    bool finalResult = false;
                    try
                    {
                        // --- BƯỚC 1: ENABLE DEVICE ---
                        if (!await Task.Run(() => connector.EnableDevice(_deviceNumber, true)))
                        {
                            _logger.LogError("Failed to enable device for set user operation. Aborting.");
                            return false;
                        }

                        // --- BƯỚC 2: THỰC HIỆN SET USER ---
                        bool result = await Task.Run(() => connector.SSR_SetUserInfo(
                            _deviceNumber, 
                            employee.employeeId.ToString(), 
                            employee.name, 
                            employee.password, 
                            employee.privilege, 
                            employee.enabled));
                            
                        if (result)
                        {
                            _logger.LogInformation("Successfully set user data for employee ID: {EmployeeId}", employee.employeeId);
                            finalResult = true;
                        }
                        else
                        {
                            int errorCode = 0;
                            connector.GetLastError(ref errorCode);
                            _logger.LogWarning("Failed to set user data for employee ID: {EmployeeId}, Error: {errorCode}", 
                                employee.employeeId, errorCode);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An exception occurred during set user process for employee ID: {EmployeeId}", 
                            employee.employeeId);
                        return false;
                    }
                    finally
                    {
                        // --- BƯỚC 3: DỌN DẸP ---
                        await Task.Run(() => connector.EnableDevice(_deviceNumber, false));
                        _logger.LogInformation("Device disabled after set user operation.");
                    }

                    return finalResult;
                }
                finally
                {
                    _deviceLock.Release();
                }
            }

            public async Task<bool> BatchSetUserAsync(List<Employee> employees)
            {
                if (!GetConnectionStatus() || !employees.Any())
                {
                    _logger.LogWarning("Cannot perform batch set: Not connected or employee list is empty.");
                    return false;
                }

                _logger.LogInformation("Starting batch set user operation for {Count} employees", employees.Count);
                
                // Mỗi thao tác hàng loạt là một "giao dịch", được bảo vệ bởi lock
                await _deviceLock.WaitAsync();
                try
                {
                    // BƯỚC 1: Mở khóa thiết bị một lần duy nhất cho thao tác batch này.
                    if (!await Task.Run(() => connector.EnableDevice(_deviceNumber, true)))
                    {
                        _logger.LogError("Failed to enable device for batch operation. Aborting.");
                        return false;
                    }

                    bool batchSuccess = false;
                    try
                    {
                        // BƯỚC 2: Báo cho thiết bị biết CHUẨN BỊ nhận dữ liệu hàng loạt.
                        if (!await Task.Run(() => connector.BeginBatchUpdate(_deviceNumber, 1))) // Tham số 1: Cập nhật dữ liệu người dùng (hoặc vân tay, theo tài liệu SDK)
                        {
                            int errorCode = 0;
                            connector.GetLastError(ref errorCode);
                            _logger.LogError("Failed to begin batch update mode on the device. Error code: {errorCode}", errorCode);
                            return false;
                        }

                        _logger.LogInformation("Device is in batch update mode. Sending user data...");
                        
                        // Lặp và thêm từng người dùng vào "bộ đệm" của SDK.
                        // Ở bước này, dữ liệu CHƯA được gửi đi.
                        foreach (var employee in employees)
                        {
                            if (!connector.SSR_SetUserInfo(_deviceNumber, employee.employeeId.ToString(), employee.name, employee.password, employee.privilege, employee.enabled))
                            {
                                // Nếu có lỗi ở đây, thường là do dữ liệu đầu vào không hợp lệ (ví dụ: tên quá dài)
                                _logger.LogWarning("Could not add user {EmployeeId} to the batch.", employee.employeeId);
                            }
                        }

                        // BƯỚC 3: GỬI toàn bộ dữ liệu trong bộ đệm lên thiết bị trong MỘT LẦN.
                        if (await Task.Run(() => connector.BatchUpdate(_deviceNumber)))
                        {
                            _logger.LogInformation("Batch update successfully committed to the device.");
                            batchSuccess = true;
                        }
                        else
                        {
                            int errorCode = 0;
                            connector.GetLastError(ref errorCode);
                            _logger.LogError("Batch update commit failed. Error code: {errorCode}", errorCode);
                        }

                        // (Tùy chọn) Gọi RefreshData một lần duy nhất sau khi batch thành công.
                        if (batchSuccess)
                        {
                            await Task.Run(() => connector.RefreshData(_deviceNumber));
                        }
                    }
                    finally
                    {
                        // BƯỚC 4: Dọn dẹp và khóa lại thiết bị, dù thành công hay thất bại.
                        // CancelBatchUpdate để đảm bảo thiết bị không bị "treo" ở chế độ batch.
                        await Task.Run(() => connector.CancelBatchUpdate(_deviceNumber)); 
                        await Task.Run(() => connector.EnableDevice(_deviceNumber, false));
                        _logger.LogInformation("Device disabled. Batch operation finished.");
                    }

                    return batchSuccess;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during the batch set user operation.");
                    return false;
                }
                finally
                {
                    _deviceLock.Release();
                }
            }

            public async Task<bool> DeleteUserAsync(int employeeId)
            {
                _logger.LogInformation("Starting BULLETPROOF deletion process for user ID: {EmployeeId}", employeeId);
                if (!GetConnectionStatus())
                {
                    _logger.LogError("Cannot delete user: Not connected to the device.");
                    return false;
                }

                await _deviceLock.WaitAsync();
                try
                {
                    if (!await Task.Run(() => connector.EnableDevice(_deviceNumber, true)))
                    {
                        _logger.LogError("Failed to enable device for delete operation. Aborting.");
                        return false;
                    }

                    bool finalResult = false;
                    try
                    {
                        // BƯỚC 1: XÓA TẤT CẢ CÁC MẪU VÂN TAY GỐC MỘT CÁCH TƯỜNG MINH
                        // Chúng ta không tin tưởng hàm xóa tổng thể sẽ làm việc này, nên ta tự làm trước.
                        // Dùng giá trị 11 như tài liệu nói để "xóa tất cả dữ liệu vân tay của người dùng".
                        _logger.LogInformation("Step 1: Explicitly deleting all fingerprint templates for user {EmployeeId} using index 11.", employeeId);
                        if (await Task.Run(() => connector.SSR_DeleteEnrollData(_deviceNumber, employeeId.ToString(), 11)))
                        {
                            _logger.LogInformation("Successfully deleted all fingerprint templates for user {EmployeeId}.", employeeId);
                        }
                        else
                        {
                            // Nếu bước này thất bại cũng không sao, bước 2 có thể sẽ dọn dẹp nốt.
                            int errorCode = 0;
                            connector.GetLastError(ref errorCode);
                            _logger.LogWarning("Could not delete all fingerprint templates using index 11 for user {EmployeeId}. Error: {ErrorCode}. Proceeding to next step.", employeeId, errorCode);
                        }

                        // BƯỚC 2: XÓA TOÀN BỘ BẢN GHI NGƯỜI DÙNG
                        // Bây giờ ta gọi hàm với tham số 12 để xóa bản ghi người dùng và những thứ khác (thẻ, mật khẩu).
                        // Kể cả bước 1 ở trên không thành công, bước này có thể sẽ dọn dẹp tất cả.
                        _logger.LogInformation("Step 2: Deleting main user record and all associated data for user {EmployeeId} using index 12.", employeeId);
                        if (await Task.Run(() => connector.SSR_DeleteEnrollData(_deviceNumber, employeeId.ToString(), 12)))
                        {
                            _logger.LogInformation("Successfully deleted main user record for ID: {EmployeeId}", employeeId);
                            finalResult = true;
                        }
                        else
                        {
                            int errorCode = 0;
                            connector.GetLastError(ref errorCode);
                            _logger.LogWarning("Failed to delete main user record for user ID: {EmployeeId}. Error: {ErrorCode}", employeeId, errorCode);
                            finalResult = false;
                        }
                    }
                    finally
                    {
                        await Task.Run(() => connector.EnableDevice(_deviceNumber, false));
                        _logger.LogInformation("Device disabled after delete operation.");
                    }
                    return finalResult;
                }
                finally
                {
                    _deviceLock.Release();
                }
            }
        #endregion
            #region User Fingerprint Methods
            public async Task<Fingerprint> GetFingerprintAsync(string employeeId, int fingerIndex)
                {
                    try
                    {
                        _logger.LogInformation("Getting fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}", 
                            employeeId, fingerIndex);
                        
                        if (!GetConnectionStatus())
                        {
                            var ex = new InvalidOperationException("Not connected to the device.");
                            _logger.LogError(ex, "Failed to get fingerprint: not connected");
                            throw ex;
                        }

                        Fingerprint fingerprint = new Fingerprint();
                        string fingerData = string.Empty;
                        int tmpLength = 0;

                        bool readResult = await Task.Run(() => connector.ReadAllTemplate(_deviceNumber));
                        if (readResult)
                        {
                            bool getResult = await Task.Run(() => connector.SSR_GetUserTmpStr(_deviceNumber, employeeId, fingerIndex, out fingerData, out tmpLength));
                            if (getResult)
                            {
                                fingerprint.employeeId = Int32.Parse(employeeId);
                                fingerprint.fingerIndex = fingerIndex;
                                fingerprint.fingerData = fingerData;
                                fingerprint.fingerLength = tmpLength;
                                _logger.LogInformation("Successfully retrieved fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}, length: {Length}",
                                    employeeId, fingerIndex, tmpLength);
                            }
                            else
                            {
                                _logger.LogWarning("Fingerprint not found for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                                    employeeId, fingerIndex);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Failed to read templates from device");
                        }
                    
                        return fingerprint;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error retrieving fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                            employeeId, fingerIndex);
                        throw;
                    }
                }
            public async Task<(bool Success, int TotalFound, int SavedCount)> GetAllFingerprintsAsync()
            {
                try
                {
                    _logger.LogInformation("Starting to get all fingerprints from device and save to database");

                    if (!GetConnectionStatus())
                    {
                        var ex = new InvalidOperationException("Not connected to the device.");
                        _logger.LogError(ex, "Failed to get fingerprints: not connected");
                        throw ex;
                    }

                    // Get all employees first
                    var employees = await GetAllEmployeeAsync();
                    if (employees == null || !employees.Any())
                    {
                        _logger.LogWarning("No employees found on device");
                        return (false, 0, 0);
                    }

                    int totalFingerprints = 0;
                    int savedFingerprints = 0;

                    // For each employee, get their fingerprints
                    foreach (var employee in employees)
                    {
                        _logger.LogInformation("Getting fingerprints for employee ID: {EmployeeId}", employee.employeeId);

                        bool readResult = await Task.Run(() => connector.ReadAllTemplate(_deviceNumber));
                        if (!readResult)
                        {
                            _logger.LogWarning("Failed to read templates for employee ID: {EmployeeId}", employee.employeeId);
                            continue;
                        }

                        // Try to get fingerprints for all possible finger indices (typically 1-10)
                        for (int fingerIndex = 0; fingerIndex <= 9; fingerIndex++)
                        {
                            string fingerData = string.Empty;
                            int tmpLength = 0;

                            bool getResult = await Task.Run(() => connector.SSR_GetUserTmpStr(
                                _deviceNumber,
                                employee.employeeId.ToString(),
                                fingerIndex,
                                out fingerData,
                                out tmpLength));

                            if (getResult && !string.IsNullOrEmpty(fingerData))
                            {
                                totalFingerprints++;

                                try
                                {
                                    var vanTay = new NhanVienVanTay
                                    {
                                        MaNhanVien = employee.employeeId,
                                        ViTriNgonTay = fingerIndex,
                                        DuLieuVanTay = fingerData
                                    };

                                    int dbResult = await _nhanVienRepository.SetNhanVienVanTay(vanTay);

                                    // For Dapper repository, we need to consider -1 as a success code
                                    // This is common in stored procedures or merge operations
                                    if (dbResult > 0 || dbResult == -1)
                                    {
                                        savedFingerprints++;
                                        _logger.LogDebug("Saved fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}, Result: {Result}",
                                            employee.employeeId, fingerIndex, dbResult);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Failed to save fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}, Result: {Result}",
                                            employee.employeeId, fingerIndex, dbResult);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error saving fingerprint to database for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                                        employee.employeeId, fingerIndex);
                                }
                            }
                        }
                    }

                    _logger.LogInformation("Finished getting fingerprints. Total found: {Total}, Successfully saved: {Saved}",
                        totalFingerprints, savedFingerprints);

                    // Consider the operation successful if we found any fingerprints, regardless of save count
                    return (totalFingerprints > 0, totalFingerprints, savedFingerprints);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting all fingerprints from device");
                    throw;
                }
            }
            public async Task<bool> SetFingerprintAsync(Fingerprint fingerprint)
            {
                // --- BƯỚC 0: KIỂM TRA KẾT NỐI ---
                _logger.LogInformation("Starting set fingerprint process for employee ID: {EmployeeId}, finger index: {FingerIndex}", 
                    fingerprint.employeeId, fingerprint.fingerIndex);
                    
                if (!GetConnectionStatus())
                {
                    _logger.LogError("Cannot set fingerprint: Not connected to the device.");
                    return false;
                }

                // Sử dụng lock để đảm bảo không có thao tác nào khác xen vào
                await _deviceLock.WaitAsync();
                try
                {
                    bool finalResult = false;
                    try
                    {
                        // --- BƯỚC 1: ENABLE DEVICE ---
                        if (!await Task.Run(() => connector.EnableDevice(_deviceNumber, true)))
                        {
                            _logger.LogError("Failed to enable device for set fingerprint operation. Aborting.");
                            return false;
                        }

                        // --- BƯỚC 2: THỰC HIỆN SET FINGERPRINT ---
                        bool result = await Task.Run(() => connector.SetUserTmpStr(
                            _deviceNumber, 
                            fingerprint.employeeId, 
                            fingerprint.fingerIndex, 
                            fingerprint.fingerData));
                        
                        if (result)
                        {
                            _logger.LogInformation("Successfully set fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                                fingerprint.employeeId, fingerprint.fingerIndex);
                            finalResult = true;
                        }
                        else
                        {
                            int errorCode = 0;
                            connector.GetLastError(ref errorCode);
                            _logger.LogWarning("Failed to set fingerprint for employee ID: {EmployeeId}, finger index: {FingerIndex}, Error: {errorCode}",
                                fingerprint.employeeId, fingerprint.fingerIndex, errorCode);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An exception occurred during set fingerprint process for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                            fingerprint.employeeId, fingerprint.fingerIndex);
                        return false;
                    }
                    finally
                    {
                        // --- BƯỚC 3: DỌN DẸP ---
                        await Task.Run(() => connector.EnableDevice(_deviceNumber, false));
                        _logger.LogInformation("Device disabled after set fingerprint operation.");
                    }

                    return finalResult;
                }
                finally
                {
                    _deviceLock.Release();
                }
            }
            public async Task<(bool Success, int SuccessCount, int FailureCount)> BatchSetFingerprintsAsync(List<Fingerprint> fingerprints)
            {
                if (!GetConnectionStatus() || !fingerprints.Any())
                {
                    _logger.LogWarning("Cannot perform batch set: Not connected or fingerprint list is empty.");
                    return (false, 0, 0);
                }

                _logger.LogInformation("Starting batch set fingerprint operation for {Count} fingerprints", fingerprints.Count);
                
                await _deviceLock.WaitAsync();
                try
                {
                    // Enable device for batch operation
                    if (!await Task.Run(() => connector.EnableDevice(_deviceNumber, true)))
                    {
                        _logger.LogError("Failed to enable device for batch operation. Aborting.");
                        return (false, 0, 0);
                    }

                    int successCount = 0;
                    int failureCount = 0;
                    bool batchSuccess = false;

                    try
                    {
                        // Begin batch update mode
                        if (!await Task.Run(() => connector.BeginBatchUpdate(_deviceNumber, 2))) // 2 for fingerprint data
                        {
                            int errorCode = 0;
                            connector.GetLastError(ref errorCode);
                            _logger.LogError("Failed to begin batch update mode on the device. Error code: {errorCode}", errorCode);
                            return (false, 0, 0);
                        }

                        _logger.LogInformation("Device is in batch update mode. Sending fingerprint data...");
                        foreach (var fingerprint in fingerprints)
                        {
                            // Validate fingerprint data
                            if (string.IsNullOrEmpty(fingerprint.fingerData))
                            {
                                _logger.LogWarning("Skipping fingerprint for employee {EmployeeId}, finger index {FingerIndex} - Empty fingerprint data",
                                    fingerprint.employeeId, fingerprint.fingerIndex);
                                failureCount++;
                                continue;
                            }

                            // Validate finger index (typically 1-10)
                            if (fingerprint.fingerIndex < 0 || fingerprint.fingerIndex > 9)
                            {
                                _logger.LogWarning("Skipping fingerprint for employee {EmployeeId}, finger index {FingerIndex} - Invalid finger index",
                                    fingerprint.employeeId, fingerprint.fingerIndex);
                                failureCount++;
                                continue;
                            }

                            // Try to add fingerprint to batch
                            bool addResult = connector.SSR_SetUserTmpStr(
                                _deviceNumber,
                                fingerprint.employeeId.ToString(),
                                fingerprint.fingerIndex,
                                fingerprint.fingerData);

                            if (!addResult)
                            {
                                int errorCode = 0;
                                connector.GetLastError(ref errorCode);
                                _logger.LogWarning("Could not add fingerprint for employee {EmployeeId}, finger index {FingerIndex} to the batch, error code {ErrorCode}",
                                    fingerprint.employeeId, fingerprint.fingerIndex, errorCode);

                                // If error is -100, it might mean the fingerprint data is invalid or corrupted
                                if (errorCode == -100)
                                {
                                    _logger.LogWarning("Fingerprint data might be invalid or corrupted for employee {EmployeeId}, finger index {FingerIndex}",
                                        fingerprint.employeeId, fingerprint.fingerIndex);
                                }
                                failureCount++;
                            }
                            else
                            {
                                _logger.LogDebug("Successfully added fingerprint for employee {EmployeeId}, finger index {FingerIndex} to batch",
                                    fingerprint.employeeId, fingerprint.fingerIndex);
                                successCount++;
                            }
                        }

                        // Commit the batch update
                        if (await Task.Run(() => connector.BatchUpdate(_deviceNumber)))
                        {
                            _logger.LogInformation("Batch update successfully committed to the device.");
                            batchSuccess = true;
                        }
                        else
                        {
                            int errorCode = 0;
                            connector.GetLastError(ref errorCode);
                            _logger.LogError("Batch update commit failed. Error code: {errorCode}", errorCode);
                            // If batch update fails, consider all fingerprints as failed
                            failureCount += successCount;
                            successCount = 0;
                        }

                        // Refresh data after successful batch
                        if (batchSuccess)
                        {
                            await Task.Run(() => connector.RefreshData(_deviceNumber));
                        }
                    }
                    finally
                    {
                        // Clean up and disable device
                        await Task.Run(() => connector.CancelBatchUpdate(_deviceNumber));
                        await Task.Run(() => connector.EnableDevice(_deviceNumber, false));
                        _logger.LogInformation("Device disabled. Batch operation finished.");
                    }

                    return (batchSuccess, successCount, failureCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An exception occurred during the batch set fingerprint operation.");
                    return (false, 0, fingerprints.Count);
                }
                finally
                {
                    _deviceLock.Release();
                }
            }

            
        #endregion
        #endregion

        #region Real-time Data Methods
        public async Task<bool> RegisterRealtimeEventAsync()
        {
            try
            {
                _logger.LogInformation("Registering realtime events");
                
                if (!GetConnectionStatus())
                {
                    var ex = new InvalidOperationException("Not connected to the device.");
                    _logger.LogError(ex, "Failed to register realtime events: not connected");
                    throw ex;
                }
                
                bool result = await Task.Run(() => connector.RegEvent(_deviceNumber, 65535));
                if (result)
                {
                    //connector.OnEnrollFinger += new _IZKEMEvents_OnEnrollFingerEventHandler(OnEnrollFingerEvent);
                    //connector.OnEnrollFingerEx += new _IZKEMEvents_OnEnrollFingerExEventHandler(OnEnrollFingerEvent);
                    //connector.OnFinger += new _IZKEMEvents_OnFingerEventHandler(OnFingerEvent);
                    connector.OnAttTransactionEx += new _IZKEMEvents_OnAttTransactionExEventHandler(OnAttTransactionEx);
                    _logger.LogInformation("Successfully registered realtime events");
                    return true;
                }
                else
                {
                    _logger.LogWarning("Failed to register realtime events");   
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering realtime events");
                throw;
            }
        }

        private async void OnAttTransactionEx(string EnrollNumber, int IsInValid, int AttState, int VerifyMethod, int Year, int Month, int Day, int Hour, int Minute, int Second, int WorkCode)
        {
            try
            {
                _logger.LogInformation("Attendance transaction received: Employee ID: {EnrollNumber}, IsValid: {IsInValid}, State: {AttState}, Method: {VerifyMethod}, DateTime: {Year}-{Month}-{Day} {Hour}:{Minute}:{Second}, WorkCode: {WorkCode}",
                    EnrollNumber, IsInValid, AttState, VerifyMethod, Year, Month, Day, Hour, Minute, Second, WorkCode);
                
                if (!GetConnectionStatus())
                {
                    var ex = new InvalidOperationException("Not connected to the device.");
                    _logger.LogError(ex, "Failed to process attendance transaction: not connected");
                    throw ex;
                }

                var attendanceTime = new DateTime(Year, Month, Day, Hour, Minute, Second);

                if(IsInValid == 0)
                {
                    int dbResult = await _chamCongRepository.SetChamCong(EnrollNumber, attendanceTime);

                    if (dbResult > 0 || dbResult == -1)
                    {
                        _logger.LogInformation("Attendance recorded successfully for employee ID: {EnrollNumber} at {AttendanceTime}", EnrollNumber, attendanceTime);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to record attendance for employee ID: {EnrollNumber}, Result: {Result}", EnrollNumber, dbResult);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in OnAttTransactionEx");
            }
        }

        public async void OnFingerEvent()
        {
            try
            {
                _logger.LogInformation("Finger event received");
                
                if (!GetConnectionStatus())
                {
                    var ex = new InvalidOperationException("Not connected to the device.");
                    _logger.LogError(ex, "Failed to process finger event: not connected");
                    throw ex;
                }
                // Handle finger event logic here
                // For example, you might want to log or process the finger index
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in OnFingerEvent");
            }
        }
        public async void OnEnrollFingerEvent(int EnrollNumber, int FingerIndex, int ActionResult, int TemplateLength)
        {
            try
            {
                _logger.LogInformation("Enroll finger event received: Employee ID: {EnrollNumber}, Finger Index: {FingerIndex}, Result: {ActionResult}, Length: {TemplateLength}",
                    EnrollNumber, FingerIndex, ActionResult, TemplateLength);
                    
                if (!GetConnectionStatus())
                {
                    var ex = new InvalidOperationException("Not connected to the device.");
                    _logger.LogError(ex, "Failed to process enrollment: not connected");
                    throw ex;
                }

                if (ActionResult == 0) // Assuming 0 means success
                {
                    _logger.LogInformation("Enrollment successful, retrieving fingerprint data");
                    var fingerprint = await GetFingerprintAsync(EnrollNumber.ToString(), FingerIndex);
                    
                    if (string.IsNullOrEmpty(fingerprint.fingerData))
                    {
                        _logger.LogWarning("Retrieved empty fingerprint data for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                            EnrollNumber, FingerIndex);
                    }
                    else
                    {
                        _logger.LogInformation("Saving fingerprint to database for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                            EnrollNumber, FingerIndex);
                            
                        try
                        {
                            var vanTay = new NhanVienVanTay
                            {
                                MaNhanVien = EnrollNumber,
                                ViTriNgonTay = FingerIndex,
                                DuLieuVanTay = fingerprint.fingerData
                            };
                            
                            int dbResult = await _nhanVienRepository.SetNhanVienVanTay(vanTay);
                            _logger.LogInformation("Database update result: {Result} rows affected", dbResult);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error saving fingerprint to database for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                                EnrollNumber, FingerIndex);
                        }
                    }
                }
                else
                {
                    var ex = new Exception($"Failed to enroll fingerprint with result code: {ActionResult}");
                    _logger.LogError(ex, "Enrollment failed for employee ID: {EmployeeId}, finger index: {FingerIndex}",
                        EnrollNumber, FingerIndex);
                    throw ex;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in OnEnrollFingerEvent");
                // Don't rethrow in event handlers as it can crash the application
            }
        }
        #endregion
    }
}