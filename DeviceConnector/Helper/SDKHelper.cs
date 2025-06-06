using DeviceConnector.Model;
using DeviceConnector.Protos;
using System.Xml.Linq;
using zkemkeeper;

namespace DeviceConnector.Helper
{
    public class SDKHelper
    {
        public CZKEM connector = new();
        private static bool _isConnected = false;
        private static string _deviceSerial = string.Empty;
        private static int _deviceNumber = 1;


        #region Connect/Disconnect Methods
        public bool GetConnectionStatus()
        {
            return _isConnected;
        }

        public void SetConnectionStatus(bool isConnected)
        {
            _isConnected = isConnected;
        }

        public int GetDeviceNumber()
        {
            return _deviceNumber;
        }

        public void SetDeviceNumber(int deviceNumber)
        {
            _deviceNumber = deviceNumber;
        }

        public string GetDeviceSerial()
        {
            if (GetConnectionStatus() == false || string.IsNullOrEmpty(_deviceSerial))
            {
                if (connector.GetSerialNumber(_deviceNumber, out _deviceSerial))
                {
                    return _deviceSerial;
                }
                return string.Empty;
            }

            if (GetConnectionStatus() == true && connector.GetSerialNumber(GetDeviceNumber(), out _deviceSerial))
            {
                return _deviceSerial;
            }
            return string.Empty;
        }

        public void SetDeviceSerial(string deviceSerial)
        {
            _deviceSerial = deviceSerial;
        }

        public void Disconnect()
        {
            if (_isConnected)
            {
                connector.Disconnect();
                SetConnectionStatus(false);
                SetDeviceSerial(string.Empty);
            }
        }

        public bool Connect(string ipAddress, int port)
        {
            if (_isConnected)
            {
                Disconnect();
                return true; // Already connected
            }
            if (connector.Connect_Net(ipAddress, port))
            {
                SetConnectionStatus(true);
                return true;
            }
            return false;
        }
        #endregion

        #region User Management Methods
            #region User Information Methods
            public List<Employee> GetAllEmployee()
            {
                if (!GetConnectionStatus())
                {
                    throw new InvalidOperationException("Not connected to the device.");
                }

                var employees = new List<Employee>();

                if (connector.ReadAllUserID(_deviceNumber))
                {
                    int dwEnrollNumber = 0;
                    string Name = string.Empty;
                    string Password = string.Empty;
                    int Privilege = 0;
                    bool Enabled = false;

                    // Move to the first user
                    bool hasUser = connector.GetAllUserInfo(_deviceNumber, ref dwEnrollNumber, ref Name, ref Password, ref Privilege, ref Enabled);
                    while (hasUser)
                    {
                        var employee = new Employee
                        {
                            employeeId = dwEnrollNumber,
                            name = Name,
                            password = Password,
                            privilege = Privilege,
                            enabled = Enabled
                        };
                        employees.Add(employee);

                        // Move to the next user
                        hasUser = connector.GetAllUserInfo(_deviceNumber, ref dwEnrollNumber, ref Name, ref Password, ref Privilege, ref Enabled);
                    }
                }
                return employees;

            }

            public Employee GetUser(int employeeId)
            {
                if (!GetConnectionStatus())
                {
                    throw new InvalidOperationException("Not connected to the device.");
                }
                Employee employee = new();
                string Name = string.Empty;
                string Password = string.Empty;
                int Privilege = 0;
                bool Enabled = false;
                if (connector.GetUserInfo(_deviceNumber, employeeId, ref Name, ref Password, ref Privilege, ref Enabled))
                {
                    employee.employeeId = employeeId;
                    employee.name = Name;
                    employee.password = Password;
                    employee.privilege = Privilege;
                    employee.enabled = Enabled;
            }
                return employee;
            }

            public bool SetUser(Employee employee)
            {
                if (!GetConnectionStatus())
                {
                    throw new InvalidOperationException("Not connected to the device.");
                }
                if (connector.SetUserInfo(_deviceNumber, employee.employeeId, employee.name, employee.password, employee.privilege, employee.enabled))
                {
                    return true;
                }
                return false;
            }
        #endregion
            #region User Fingerprint Methods
            public Fingerprint GetFingerprint(int employeeId, int fingerIndex)
            {
                if (!GetConnectionStatus())
                {
                    throw new InvalidOperationException("Not connected to the device.");
                }


                Fingerprint fingerprint = new Fingerprint();
                string fingerData = string.Empty;
                int tmpLength = 0;

                if (connector.ReadAllTemplate(_deviceNumber))
                {
                    if (connector.GetUserTmpStr(_deviceNumber, employeeId, fingerIndex, ref fingerData, ref tmpLength))
                    {
                        fingerprint.employeeId = employeeId;
                        fingerprint.fingerIndex = fingerIndex;
                        fingerprint.fingerData = fingerData;
                        fingerprint.fingerLength = tmpLength;
                }
                }
                return fingerprint;
            }

            public bool SetFingerprint(Fingerprint fingerprint)
            {
                if (!GetConnectionStatus())
                {
                    throw new InvalidOperationException("Not connected to the device.");
                }
                if (connector.SetUserTmpStr(_deviceNumber, fingerprint.employeeId, fingerprint.fingerIndex, fingerprint.fingerData))
                {
                    return true;
                }
                return false;
            }
        #endregion
        #endregion

        #region Real-time Data Methods
        public bool RegisterRealtimeEvent()
        {
            if (!GetConnectionStatus())
            {
                throw new InvalidOperationException("Not connected to the device.");
            }
            if (connector.RegEvent(_deviceNumber, 65535))
            {
                connector.OnEnrollFinger += OnEnrollFingerEvent;
                return true;
            }
            return false;
        }

        public void OnEnrollFingerEvent(int employeeId, int fingerIndex, int result, int tmpLength)
        {
            if (!GetConnectionStatus())
            {
                throw new InvalidOperationException("Not connected to the device.");
            }

            if (result == 0) // Assuming 0 means success
            {
                var fingerprint = GetFingerprint(employeeId, fingerIndex);
                SetFingerprint(fingerprint);
            }
            else
            {
                throw new Exception("Failed to enroll fingerprint.");
            }
        }
        #endregion
    }
}
