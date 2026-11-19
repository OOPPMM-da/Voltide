
using System;
using System.Collections.Generic;
using System.Threading;
using Voltide.Core;
using Voltide.Interop;

namespace Voltide.Providers
{
    /// <summary>
    /// ATK F1 Ultimate 2.0 2.4G Mouse
    ///
    /// HID identity from local device report:
    /// VID 373B / PID 11D9
    /// UsagePage FF02 / Usage 0002
    /// Input/Output: 17/17
    ///
    /// Experimental protocol:
    /// Reuses the ATK FF02 17-byte battery protocol
    /// already implemented for A9 Airadd.
    /// F1 Ultimate 2.0 still requires hardware verification.
    /// </summary>
    public class AtkF1Ultimate2Provider : IDeviceProvider
    {
        private const ushort AtkVid = 0x373B;
        private const ushort F1Pid = 0x11D9;

        private const ushort VendorPage = 0xFF02;
        private const ushort VendorUsage = 0x0002;

        private const int FrameSize = 17;

        private const byte ReportId = 0x08;
        private const byte CmdBattery = 0x04;
        private const byte FrameSum = 0x4D;

        private const int PercentIndex = 6;
        private const int ChargingIndex = 7;
        private const int VoltageIndex = 8;

        public string Name
        {
            get { return "ATK F1 Ultimate 2.0"; }
        }

        public List<DeviceInfo> Discover(List<HidDeviceInfo> catalog)
        {
            List<DeviceInfo> found = new List<DeviceInfo>();

            if (catalog == null)
                return found;

            foreach (HidDeviceInfo h in catalog)
            {
                if (h.VendorId != AtkVid) continue;
                if (h.ProductId != F1Pid) continue;

                if (h.UsagePage != VendorPage) continue;
                if (h.Usage != VendorUsage) continue;

                if (h.InputReportLength != FrameSize) continue;
                if (h.OutputReportLength != FrameSize) continue;

                DeviceInfo d = new DeviceInfo();

                d.Id = "atk-f1-ultimate-2";
                d.Brand = "ATK";
                d.Model = "F1 Ultimate 2.0";
                d.Name = "ATK F1 Ultimate 2.0";
                d.ShortModel = "F1 ULT 2.0";

                d.Kind = DeviceKind.Mouse;

                d.VendorId = h.VendorId;
                d.ProductId = h.ProductId;

                d.HidPath = h.Path;
                d.ProviderName = Name;

                found.Add(d);
                break;
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

            if (device == null ||
                string.IsNullOrEmpty(device.HidPath))
            {
                r.Error = "ATK HID 路径无效";
                return r;
            }

            try
            {
                using (HidSession s = new HidSession(
                    device.HidPath, FrameSize, FrameSize))
                {
                    s.Flush(30);

                    byte[] request = NewQuery();
                    byte[] reply = null;

                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        if (attempt > 0)
                            Thread.Sleep(120);

                        // This protocol uses HID SET_REPORT.
                        if (!s.SetOutputReport(request))
                        {
                            r.Error = "ATK 电量查询发送失败";
                            return r;
                        }

                        reply = WaitFor(s, 700);

                        if (reply != null)
                            break;
                    }

                    if (reply == null)
                    {
                        r.Sleeping = true;
                        r.Error = "ATK 鼠标休眠或未响应";
                        return r;
                    }

                    if (reply.Length <= VoltageIndex + 1 ||
                        reply[0] != ReportId ||
                        reply[1] != CmdBattery)
                    {
                        r.Error = "ATK 回复格式不匹配";
                        return r;
                    }

                    int percent = reply[PercentIndex];

                    if (percent > 100)
                    {
                        r.Online = true;
                        r.Error = "ATK 电量数据无效";
                        return r;
                    }

                    int voltage =
                        (reply[VoltageIndex] << 8) |
                        reply[VoltageIndex + 1];

                    byte charge = reply[ChargingIndex];

                    r.Raw = BitConverter.ToString(reply);
                    r.Percent = percent;
                    r.Level = BatteryMath.FromPercent(percent);

                    r.Online = true;

                    if (charge == 0 || charge == 1)
                    {
                        r.ChargingKnown = true;
                        r.Charging = charge == 1;
                    }

                    r.Detail = "2.4G · " + voltage + " mV";
                    r.Error = null;
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }

            return r;
        }

        private static byte[] NewQuery()
        {
            byte[] query = new byte[FrameSize];

            query[0] = ReportId;
            query[1] = CmdBattery;

            int sum = 0;

            for (int i = 1; i < FrameSize - 1; i++)
                sum += query[i];

            query[FrameSize - 1] =
                (byte)((FrameSum - sum) & 0xFF);

            return query;
        }

        private static byte[] WaitFor(HidSession s, int timeoutMs)
        {
            DateTime deadline =
                DateTime.Now.AddMilliseconds(timeoutMs);

            while (DateTime.Now < deadline)
            {
                byte[] p = s.Read(100);

                if (p == null)
                    continue;

                if (p.Length >= 10 &&
                    p[0] == ReportId &&
                    p[1] == CmdBattery)
                {
                    return p;
                }
            }

            return null;
        }
    }
}
