
using System;
using System.Collections.Generic;
using System.Threading;
using Voltide.Core;
using Voltide.Interop;

namespace Voltide.Providers
{
    /// <summary>
    /// XIBERIA K06S Wireless Headset
    ///
    /// HID identity from local device report:
    /// VID 36F9 / PID F002
    /// UsagePage FF90 / Usage 0001
    /// Input/Output: 64/64
    ///
    /// Experimental read-only candidate:
    /// Report ID 55, command 65, target 01.
    ///
    /// The K06S-specific command and response layout
    /// still require verification on the real headset.
    /// </summary>
    public class XiberiaK06sProvider : IDeviceProvider
    {
        private const ushort XiberiaVid = 0x36F9;
        private const ushort K06sPid = 0xF002;

        private const ushort VendorPage = 0xFF90;
        private const ushort VendorUsage = 0x0001;

        private const int FrameSize = 64;

        private const byte ReportId = 0x55;
        private const byte CmdBattery = 0x65;
        private const byte TargetHeadset = 0x01;

        private const int PercentIndex = 2;
        private const int ChargeStateIndex = 3;

        private const byte StateDischarging = 0x02;
        private const byte StateCharging = 0x03;
        private const byte StateFull = 0x04;

        public string Name
        {
            get { return "XIBERIA K06S"; }
        }

        public List<DeviceInfo> Discover(List<HidDeviceInfo> catalog)
        {
            List<DeviceInfo> found = new List<DeviceInfo>();

            if (catalog == null)
                return found;

            foreach (HidDeviceInfo h in catalog)
            {
                if (h.VendorId != XiberiaVid) continue;
                if (h.ProductId != K06sPid) continue;

                if (h.UsagePage != VendorPage) continue;
                if (h.Usage != VendorUsage) continue;

                if (h.InputReportLength != FrameSize) continue;
                if (h.OutputReportLength != FrameSize) continue;

                DeviceInfo d = new DeviceInfo();

                d.Id = "xiberia-k06s";
                d.Brand = "XIBERIA";
                d.Model = "K06S";
                d.Name = "XIBERIA K06S Wireless";
                d.ShortModel = "K06S";

                d.Kind = DeviceKind.Headset;

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
            r.Detail = "2.4G · XIBERIA K06S";

            if (device == null ||
                string.IsNullOrEmpty(device.HidPath))
            {
                r.Error = "K06S HID 路径无效";
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

                        // Candidate uses interrupt OUT / IN.
                        if (!s.Write(request, 250))
                        {
                            r.Error = "K06S 查询命令发送失败";
                            return r;
                        }

                        reply = WaitFor(s, 650);

                        if (reply != null)
                            break;
                    }

                    if (reply == null)
                    {
                        r.Sleeping = true;
                        r.Error = "K06S 未响应，可能休眠或协议不匹配";
                        return r;
                    }

                    if (reply.Length < 4 ||
                        reply[0] != ReportId ||
                        reply[1] != CmdBattery)
                    {
                        r.Error = "K06S 回复格式不匹配";
                        return r;
                    }

                    int percent = reply[PercentIndex];
                    byte state = reply[ChargeStateIndex];

                    if (percent > 100)
                    {
                        r.Online = true;
                        r.Error = "K06S 电量数据无效";
                        return r;
                    }

                    // Only accept the three known state values.
                    if (state != StateDischarging &&
                        state != StateCharging &&
                        state != StateFull)
                    {
                        r.Online = true;
                        r.Error = "K06S 充电状态未匹配";
                        return r;
                    }

                    if (state == StateFull)
                        percent = 100;

                    r.Raw = BitConverter.ToString(
                        reply, 0, Math.Min(reply.Length, 16));

                    r.Percent = percent;
                    r.Level = BatteryMath.FromPercent(percent);

                    r.Online = true;
                    r.ChargingKnown = true;
                    r.Charging = state == StateCharging;

                    if (state == StateCharging)
                        r.Detail = "2.4G · 充电中";
                    else if (state == StateFull)
                        r.Detail = "2.4G · 已充满";

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
            query[2] = TargetHeadset;

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

                if (p.Length >= 4 &&
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
