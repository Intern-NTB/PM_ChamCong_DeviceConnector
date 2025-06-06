namespace DeviceConnector.Model
{
    public class Fingerprint
    {
        public int employeeId { get; set; } // Employee ID associated with the fingerprint
        public int fingerIndex { get; set; } // Index of the finger (e.g., 1 for thumb, 2 for index finger, etc.)
        public string fingerData { get; set; } // Binary data representing the fingerprint template
        public int fingerLength { get; set; } // Length of the fingerprint data in bytes
    }
}
