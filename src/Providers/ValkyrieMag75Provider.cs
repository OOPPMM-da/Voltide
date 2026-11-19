
using System;
using System.Collections.Generic;
using System.Threading;
using Voltide.Core;
using Voltide.Interop;

namespace Voltide.Providers
{
    /// <summary>
    /// VALKYRIE VK MAG 75 MAX 2.4G keyboard.
    ///
    /// Hardware verified:
    /// VID: 0x374A
    /// PID: 0xA223
    /// UsagePage: 0xFFFF
    /// Usage: 0x0002
    /// FeatureReportLength: 65
    ///
    /// Receiver status command: 0xF7
    /// Battery percentage: reply[1]
    /// Keyboard online: reply[3] == 0
    /// Device kind: reply[6]
    ///
    /// Windows HID Feature Report:
    /// 65 bytes = Report ID + 64-byte protocol packet.
    /// </summary>
    public class ValkyrieMag75Provider : IDeviceProvider
    {
        private const ushort VendorId = 0x374A;
        private const ushort ProductId = 0xA223;

        private const ushort UsagePage = 0xFFFF;
        private const ushort Usage = 0x0002;

        private const int ReportLength = 65;

        private const byte CmdStatus = 0xF7;

        public string Name
        {
            get { return "VALKYRIE HID"; }
        }

        public List<DeviceInfo> Discover(
            List<HidDeviceInfo> catalog)
        {
            List<DeviceInfo> found =
                new List<DeviceInfo>();

            if (catalog == null)
                return found;

            HashSet<string> paths =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

            foreach (HidDeviceInfo h in catalog)
            {
                if (h.VendorId != VendorId) continue;
                if (h.ProductId != ProductId) continue;

                if (h.UsagePage != UsagePage) continue;
                if (h.Usage != Usage) continue;

                if (h.FeatureReportLength != ReportLength)
                    continue;

                if (string.IsNullOrEmpty(h.Path))
                    continue;

                if (!paths.Add(h.Path))
                    continue;

                DeviceInfo d = new DeviceInfo();

                d.Id = "valkyrie-vk-mag-75-max";
                d.Brand = "VALKYRIE";
                d.Model = "VK MAG 75 MAX";
                d.Name = "VALKYRIE VK MAG 75 MAX";
                d.ShortModel = "VK MAG 75 MAX";

                d.Kind = DeviceKind.Keyboard;

                d.VendorId = h.VendorId;
                d.ProductId = h.ProductId;

                d.HidPath = h.Path;
                d.ProviderName = Name;

                found.Add(d);
            }

            return found;
        }

        public DeviceReading Read(DeviceInfo device)
        {
            DeviceReading r = new DeviceReading();

            r.UpdatedAt = DateTime.Now;
            r.Online = false;
            r.Level = BatteryLevel.Unknown;
            r.ChargingKnown = false;
            r.Detail = "2.4G · VK MAG 75 MAX";

            IntPtr handle = IntPtr.Zero;

            try
            {
                handle = HidNative.CreateFile(
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
                    handle = IntPtr.Zero;
                    throw new InvalidOperationException(
                        "无法打开 VK MAG 75 MAX HID 接口");
                }

                // Build the 65-byte Feature Report.
                byte[] tx = new byte[ReportLength];

                // Report ID = 0.
                tx[0] = 0x00;

                // Protocol command F7.
                tx[1] = CmdStatus;

                // Bit7 checksum:
                // byte 8 = 0xFF - sum(byte 1..7).
                int sum = 0;

                for (int i = 1; i <= 7; i++)
                    sum += tx[i];

                tx[8] = (byte)(
                    0xFF - (sum & 0xFF));

                // Send receiver status request.
                if (!HidNative.HidD_SetFeature(
                    handle, tx, tx.Length))
                {
                    throw new InvalidOperationException(
                        "发送 F7 状态查询失败");
                }

                Thread.Sleep(20);

                // Read receiver status.
                byte[] rx = new byte[ReportLength];
                rx[0] = 0x00;

                if (!HidNative.HidD_GetFeature(
                    handle, rx, rx.Length))
                {
                    throw new InvalidOperationException(
                        "读取 F7 状态回复失败");
                }

                // Remove Windows HID Report ID byte.
                byte[] p = new byte[64];

                Buffer.BlockCopy(
                    rx, 1, p, 0, 64);

                r.Raw = BitConverter.ToString(p, 0, 9)
                    .Replace("-", " ");

                // Device type:
                // 1 = keyboard
                // 2 = mouse
                // 3 = keyboard + mouse
                bool hasKeyboard =
                    p[6] == 1 || p[6] == 3;

                if (!hasKeyboard)
                {
                    r.Error = "接收器未检测到已配对键盘";
                    r.Sleeping = true;
                    return r;
                }

                int percent = p[1];

                if (percent > 100)
                {
                    r.Error = "电量数据异常";
                    return r;
                }

                r.Percent = percent;
                r.Level = BatteryMath.FromPercent(percent);

                // Receiver reports keyboard online when byte 3 is zero.
                r.Online = p[3] == 0;

                if (!r.Online)
                {
                    r.Sleeping = true;
                    r.Error = "键盘未在线或处于休眠状态";
                    return r;
                }

                // F7 status does not establish charging state.
                r.Charging = false;
                r.ChargingKnown = false;

            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }
            finally
            {
                if (handle != IntPtr.Zero)
                    HidNative.CloseHandle(handle);
            }

            return r;
        }
    }
}
