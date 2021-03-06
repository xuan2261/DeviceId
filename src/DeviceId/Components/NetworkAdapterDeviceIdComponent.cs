﻿using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace DeviceId.Components
{
    /// <summary>
    /// An implementation of <see cref="IDeviceIdComponent"/> that retrieves data from installed network adapaters.
    /// </summary>
    /// <remarks>
    /// Based on Win32_NetworkAdapter WMI class or using the CIMv2 based MSFT_NetAdapter WMI class (Windows 8 and up only).
    /// Optionally filters out non physical network adapters (not related to virtual machines) and wireless adapters.
    /// </remarks>
    public class NetworkAdapterDeviceIdComponent : IDeviceIdComponent
    {
        /// <summary>
        /// Gets the name of the component.
        /// </summary>
        public string Name { get; } = "MACAddress";

        /// <summary>
        /// A value indicating whether non-physical adapters should be excluded.
        /// </summary>

        private readonly bool _excludeNonPhysical;

        /// <summary>
        /// A value indicating whether wireless adapters should be excluded.
        /// </summary>

        private readonly bool _excludeWireless;

        /// <summary>
        /// Initializes a new instance of the <see cref="NetworkAdapterDeviceIdComponent"/> class.
        /// </summary>
        /// <param name="excludeNonPhysical">A value indicating whether non-physical adapters should be excluded.</param>
        /// <param name="excludeWireless">Indicates if wireless adapters should be excluded.</param>
        /// <remarks>
        /// Non physical adapters are unlikely to have a stable MAC address.
        /// For wireless adapters MAC randomization is a frequently offered function, making it unsuitable for identifying a device.
        /// </remarks>
        public NetworkAdapterDeviceIdComponent(bool excludeNonPhysical, bool excludeWireless)
        {
            _excludeNonPhysical = excludeNonPhysical;
            _excludeWireless = excludeWireless;
        }

        /// <summary>
        /// Gets the component value.
        /// </summary>
        /// <returns>The component value.</returns>
        public string GetValue()
        {
            List<string> values;

            try
            {
                // First attempt to retrieve the addresses using the CIMv2 interface.
                values = GetMacAddressesUsingCimV2();
            }
            catch (ManagementException ex)
            {
                // In case we are notified of an invalid namespace, attempt to lookup the adapters using WMI.
                // Could avoid this catch by manually checking for the CIMv2 namespace.

                if (ex.ErrorCode == ManagementStatus.InvalidNamespace)
                {
                    values = GetMacAddressesUsingWmi();
                }
                else
                {
                    throw;
                }
            }

            return string.Join(",", values);
        }

        /// <summary>
        /// Retrieves the MAC addresses using the (old) Win32_NetworkAdapter WMI class.
        /// </summary>
        /// <returns>A list of MAC addresses.</returns>
        internal List<string> GetMacAddressesUsingWmi()
        {
            var values = new List<string>();

            using var mc = new ManagementClass("Win32_NetworkAdapter");

            foreach (var adapter in mc.GetInstances())
            {
                try
                {
                    var isPhysical = (bool)adapter["PhysicalAdapter"];
                    var adapterType = adapter["AdapterType"] as string;

                    // Skip non physcial adapters if instructed to do so.
                    if (_excludeNonPhysical && !isPhysical)
                    {
                        continue;
                    }

                    // Add the MAC address to the list of values.
                    var value = adapter["MACAddress"] as string;
                    if (value != null)
                    {
                        values.Add(value);
                    }
                }
                finally
                {
                    adapter.Dispose();
                }
            }

            return values;
        }

        /// <summary>
        /// Retrieves the MAC addresses using the CIMv2 based MSFT_NetAdapter interface (Windows 8 and up).
        /// </summary>
        /// <returns>A list of MAC addresses.</returns>
        internal List<string> GetMacAddressesUsingCimV2()
        {
            var values = new List<string>();

            using var mc = new ManagementClass("root/StandardCimv2", "MSFT_NetAdapter", new ObjectGetOptions { });

            foreach (var adapter in mc.GetInstances())
            {
                try
                {
                    var isPhysical = (bool)adapter["ConnectorPresent"];
                    var ndisMedium = (uint)adapter["NdisPhysicalMedium"];

                    // Skip non physcial adapters if instructed to do so.
                    if (_excludeNonPhysical && !isPhysical)
                    {
                        continue;
                    }

                    // Skip wireless adapters if instructed to do so.
                    if (_excludeWireless && ndisMedium == 9) // Native802_11
                    {
                        continue;
                    }

                    // Add the MAC address to the list of values.
                    var value = adapter["PermanentAddress"] as string;
                    if (value != null)
                    {
                        // Ensure the hardware addresses are formatted as MAC addresses if possible.
                        // This is a discrepancy between the MSFT_NetAdapter and Win32_NetworkAdapter interfaces.
                        value = FormatMacAddress(value);
                        values.Add(value);
                    }
                }
                finally
                {
                    adapter.Dispose();
                }
            }

            return values;
        }

        /// <summary>
        /// Formats the specified MAC address.
        /// </summary>
        /// <param name="input">The MAC address to format.</param>
        /// <returns>The formatted MAC address.</returns>
        internal static string FormatMacAddress(string input)
        {
            // Check if this can be a hex formatted EUI-48 or EUI-64 identifier.
            if (input.Length != 12 && input.Length != 16)
            {
                return input;
            }

            // Chop up input in 2 character chunks.
            var partSize = 2;
            var parts = Enumerable.Range(0, input.Length / partSize).Select(x => input.Substring(x * partSize, partSize));

            // Put the parts in the AA:BB:CC format.
            var result = string.Join(":", parts);

            return result;
        }
    }
}
