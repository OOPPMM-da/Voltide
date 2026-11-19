
using System;
using System.Collections.Generic;
using Voltide.Core;
using Voltide.Interop;

namespace Voltide.Providers
{
    /// <summary>
    /// WEIKAV NUT75 2.4G Wireless Keyboard
    ///
    /// VID: 0C45
    /// PID: FEF9
    ///
    /// Preferred interface:
    /// UsagePage FF70 / Usage 0071
    ///
    /// Alternate interface:
    /// UsagePage FF60 / Usage 0061
    ///
    /// HFD/AULA receiver protocol:
    /// Report ID: 00
    /// Request magic: AA
    /// Response magic: 55
    /// GET_DEVICE_INFO: 10
    /// Response battery: payload[17]
    /// Response charge: payload[18]
    ///
    /// Read-only query. No settings are modified.
    /// </summary>
    public class Nut75HfdProvider : IDeviceProvider
    {
        private const ushort HfdVid = 0x0C45;
        private const ushort Nut75Pid = 0xFEF9;

        private const ushort PagePreferred = 0xFF70;
        private const ushort UsagePreferred = 0x0071;

        private const ushort PageAlternate = 0xFF60;
        private const ushort UsageAlternate = 0x0061;

        private const int FrameSize = 65;
        private const int PayloadSize = 56;

        private const byte ReportId = 0x00;
        private const byte OutMagic = 0xAA;
        private const byte InMagic = 0x55;
        private const byte CmdDeviceInfo = 0x10;

        private const int WorkModeIndex = 16;
        private const int BatteryIndex = 17;
        private const int ChargingIndex = 18;

        public string Name
        {
            get { return "WEIKAV NUT75 HFD"; }
        }

        public List<DeviceInfo> Discover(List<HidDeviceInfo> catalog)
        {
            List<DeviceInfo> found = new List<DeviceInfo>();

            if (catalog == null)
                return found;

            HidDeviceInfo selected = null;

            foreach (HidDeviceInfo h in catalog)
            {
                if (h.VendorId != HfdVid) continue;
                if (h.ProductId != Nut75Pid) continue;

                if (h.InputReportLength != FrameSize) continue;
                if (h.OutputReportLength != FrameSize) continue;

                bool preferred =
                    h.UsagePage == PagePreferred &&
                    h.Usage == UsagePreferred;

                bool alternate =
                    h.UsagePage == PageAlternate &&
                    h.Usage == UsageAlternate;

                if (!preferred && !alternate)
                    continue;

                // Prefer FF70. Fall back to FF60 if necessary.
                if (selected == null ||
                    preferred)
                {
                    selected = h;
                }

                if (preferred)
                    break;
            }

            if (selected == null)
                return found;

            DeviceInfo d = new DeviceInfo();

            d.Id = "nut75-2.4g";
            d.Brand = "WEIKAV";
            d.Model = "NUT75 2.4G";
            d.Name = "WEIKAV NUT75 2.4G";
            d.ShortModel = "NUT75";

            d.Kind = DeviceKind.Keyboard;

            d.VendorId = selected.VendorId;
            d.ProductId = selected.ProductId;

            d.HidPath = selected.Path;
            d.ProviderName = Name;

            found.Add(d);

            return found;
        }

        public DeviceReading Read(DeviceInfo device)
        {
            DeviceReading r = new DeviceReading();

            r.UpdatedAt = DateTime.Now;
            r.Online = false;
            r.Level = BatteryLevel.Unknown;
            r.ChargingKnown = false;
            r.Detail = "2.4G · NUT75";

            if (device == null ||
                string.IsNullOrEmpty(device.HidPath))
            {
                r.Error = "NUT75 HID 路径无效";
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
                        if (!s.Write(request, 250))
                        {
                            r.Error = "NUT75 查询命令发送失败";
                            return r;
                        }

                        reply = WaitFor(s, 450);

                        if (reply != null)
                            break;
                    }

                    if (reply == null)
                    {
                        r.Sleeping = true;
                        r.Error = "NUT75 未响应，可能休眠或未连接";
                        return r;
                    }

                    int percent;
                    int workMode;
                    byte chargeStatus;

                    if (!TryParse(
                        reply,
                        out percent,
                        out workMode,
                        out chargeStatus))
                    {
                        r.Error = "NUT75 HFD 回复数据无效";
                        return r;
                    }

                    // Mode 0 is wired USB. Do not report it as
                    // a 2.4G receiver battery reading.
                    if (workMode == 0)
                    {
                        r.Error = "键盘当前不处于无线模式";
                        return r;
                    }

                    // HFD:
                    // 0 = no charging
                    // 1 = charging
                    // 2 = full
                    if (chargeStatus == 2)
                        percent = 100;

                    r.Raw = BitConverter.ToString(
                        reply, 0, Math.Min(reply.Length, 32));

                    r.Percent = percent;
                    r.Level = BatteryMath.FromPercent(percent);

                    r.Online = true;

                    if (chargeStatus <= 2)
                    {
                        r.ChargingKnown = true;
                        r.Charging = chargeStatus == 1;
                    }

                    r.Detail = workMode == 2
                        ? "2.4G"
                        : "无线模式";

                    if (chargeStatus == 1)
                        r.Detail += " · 充电中";
                    else if (chargeStatus == 2)
                        r.Detail += " · 已充满";

                    r.Error = null;
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }

            return r;
        }

        /// <summary>
        /// 65-byte HFD GET_DEVICE_INFO request.
        ///
        /// Byte 0: Report ID 00
        /// Byte 1: AA
        /// Byte 2: command 10
        /// Byte 3: payload length 56
        /// Byte 7: final packet flag 01
        /// </summary>
        private static byte[] NewQuery()
        {
            byte[] request = new byte[FrameSize];

            request[0] = ReportId;
            request[1] = OutMagic;
            request[2] = CmdDeviceInfo;
            request[3] = PayloadSize;
            request[7] = 0x01;

            return request;
        }

        private static byte[] WaitFor(
            HidSession s,
            int timeoutMs)
        {
            DateTime deadline =
                DateTime.Now.AddMilliseconds(timeoutMs);

            while (DateTime.Now < deadline)
            {
                byte[] p = s.Read(60);

                if (p == null)
                    continue;

                if (p.Length < 28)
                    continue;

                if (p[0] != ReportId ||
                    p[1] != InMagic ||
                    p[2] != CmdDeviceInfo)
                    continue;

                int payloadLength = p[3];

                if (payloadLength < 19 ||
                    payloadLength > PayloadSize)
                    continue;

                if (p[4] != 0 || p[5] != 0)
                    continue;

                if (p.Length < 9 + payloadLength)
                    continue;

                return p;
            }

            return null;
        }

        private static bool TryParse(
            byte[] reply,
            out int percent,
            out int workMode,
            out byte chargeStatus)
        {
            percent = 0;
            workMode = 0;
            chargeStatus = 0xFF;

            if (reply == null || reply.Length < 28)
                return false;

            if (reply[0] != ReportId ||
                reply[1] != InMagic ||
                reply[2] != CmdDeviceInfo)
                return false;

            int payloadLength = reply[3];

            if (payloadLength < 19 ||
                payloadLength > PayloadSize)
                return false;

            if (reply.Length < 9 + payloadLength)
                return false;

            int offset = 9;

            workMode = reply[offset + WorkModeIndex];
            percent = reply[offset + BatteryIndex];
            chargeStatus = reply[offset + ChargingIndex];

            if (percent < 0 || percent > 100)
                return false;

            if (workMode > 2)
                return false;

            return true;
        }
    }
}
