
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Voltide.Core;
using Voltide.Interop;

namespace Voltide.Providers
{
    /// <summary>
    /// VALKYRIE VK MAG 75 MAX
    ///
    /// Verified hardware:
    /// VID: 374A
    /// PID: A223
    /// Interface: MI_02
    /// UsagePage: FFFF
    /// Usage: 0002
    /// FeatureReportLength: 65
    ///
    /// Battery query:
    /// 00 F7 00 00 00 00 00 00 08 ...
    /// </summary>
    public class ValkyrieVkMag75MaxProvider : IDeviceProvider
    {
        private const ushort VendorId = 0x374A;
        private const ushort ProductId = 0xA223;

        private const ushort VendorUsagePage = 0xFFFF;
        private const ushort VendorUsage = 0x0002;

        private const int ReportLength = 65;

        private const byte ReportId = 0x00;
        private const byte Command = 0xF7;
        private const byte Checksum = 0x08;

        public string Name
        {
            get { return "VALKYRIE VK MAG 75 MAX"; }
        }

        /// <summary>
        /// Find VK MAG 75 MAX receiver interface.
        /// </summary>
        public List<DeviceInfo> Discover(List<HidDeviceInfo> catalog)
        {
            List<DeviceInfo> found = new List<DeviceInfo>();

            if (catalog == null)
                return found;

            foreach (HidDeviceInfo h in catalog)
            {
                if (h.VendorId != VendorId)
                    continue;

                if (h.ProductId != ProductId)
                    continue;

                if (h.UsagePage != VendorUsagePage)
                    continue;

                if (h.Usage != VendorUsage)
                    continue;

                if (h.FeatureReportLength != ReportLength)
                    continue;

                if (!IsTargetInterface(h.Path))
                    continue;

                DeviceInfo d = new DeviceInfo();

                d.Id = "valkyrie-vk-mag75max-" + h.Path;
                d.Brand = "VALKYRIE";
                d.Model = "VK MAG 75 MAX";
                d.Name = "VALKYRIE VK MAG 75 MAX";
                d.ShortModel = "MAG 75 MAX";

                d.Kind = DeviceKind.Keyboard;

                d.VendorId = h.VendorId;
                d.ProductId = h.ProductId;

                d.HidPath = h.Path;
                d.ProviderName = Name;

                found.Add(d);
            }

            return found;
        }

        private static bool IsTargetInterface(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return path.IndexOf(
                "&MI_02#",
                StringComparison.OrdinalIgnoreCase) >= 0
                ||
                path.IndexOf(
                    "&MI_02&",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Read receiver battery status.
        /// </summary>
        public DeviceReading Read(DeviceInfo device)
        {
            DeviceReading r = new DeviceReading();

            r.UpdatedAt = DateTime.Now;
            r.Level = BatteryLevel.Unknown;
            r.ChargingKnown = false;

            if (device == null ||
                string.IsNullOrEmpty(device.HidPath))
            {
                r.Error = "HID 设备路径无效";
                return r;
            }

            IntPtr handle = HidNative.CreateFile(
                device.HidPath,
                HidNative.GENERIC_READ |
                HidNative.GENERIC_WRITE,
                HidNative.FILE_SHARE_RW,
                IntPtr.Zero,
                HidNative.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle == new IntPtr(-1))
            {
                r.Error = "打开 HID 设备失败，错误码：" +
                    Marshal.GetLastWin32Error();

                return r;
            }

            try
            {
                // Build 65-byte Feature Report.
                byte[] request = new byte[ReportLength];

                request[0] = ReportId;
                request[1] = Command;
                request[8] = Checksum;

                // Send receiver status query.
                if (!HidNative.HidD_SetFeature(
                    handle,
                    request,
                    request.Length))
                {
                    r.Error = "电量查询发送失败";
                    return r;
                }

                Thread.Sleep(20);

                // Read receiver status.
                byte[] reply = new byte[ReportLength];
                reply[0] = ReportId;

                if (!HidNative.HidD_GetFeature(
                    handle,
                    reply,
                    reply.Length))
                {
                    r.Error = "电量读取失败";
                    return r;
                }

                if (reply[0] != ReportId)
                {
                    r.Error = "Report ID 不匹配";
                    return r;
                }

                // Byte 6 of protocol payload:
                // 1 = keyboard
                // 2 = mouse
                // 3 = keyboard and mouse
                if (reply[7] != 1 && reply[7] != 3)
                {
                    r.Error = "接收器未报告键盘设备";
                    return r;
                }

                // Keyboard battery: protocol payload byte 1.
                int percent = reply[2];

                if (percent > 100)
                {
                    r.Error = "电量数据无效";
                    return r;
                }

                // Keyboard online:
                // protocol payload byte 3 == 0.
                r.Online = (reply[4] == 0);

                r.Percent = percent;
                r.Level = BatteryMath.FromPercent(percent);

                // Charging status has not been verified.
                r.Charging = false;
                r.ChargingKnown = false;

                r.Detail = "2.4G 无线接收器";

                r.Raw = BitConverter.ToString(
                    reply, 0, 16);

                r.Error = null;

                return r;
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
                return r;
            }
            finally
            {
                HidNative.CloseHandle(handle);
            }
        }
    }
}
