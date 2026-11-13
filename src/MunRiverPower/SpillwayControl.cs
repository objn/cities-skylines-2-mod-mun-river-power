using System;
using System.Globalization;

namespace MunRiverPower
{
    internal enum SpillwayStatus { Monitoring, Closed, Releasing, Disabled, NoWater, Unsupported, Waiting, Blocked }
    internal sealed class FreshWaterReadback
    {
        private bool seen;
        private uint last;
        internal bool TryConsume(uint stamp)
        {
            if (seen && stamp == last) return false;
            seen = true; last = stamp; return true;
        }
        internal void Reset() { seen = false; last = 0; }
    }
    internal static class SpillwayControl
    {
        internal const string UpgradeName = "MunRiverPower.AutomaticSpillway";
        internal const double OpenFreeboard = 0.5;
        internal const double CloseFreeboard = 1.0;
        internal const double CloseTolerance = 0.005; // Avoid an asymptotic gate that never closes due to float precision.
        internal const double MaximumDepthPerReading = 0.025;
        internal const uint UpgradeCost = 100000;
        internal const int MonthlyUpkeep = 1000;
        internal static bool NextOpen(bool open, double freeboard, bool enabled, bool valid)
        {
            if (!enabled || !valid || double.IsNaN(freeboard) || double.IsInfinity(freeboard)) return false;
            return open ? freeboard < CloseFreeboard - CloseTolerance : freeboard <= OpenFreeboard;
        }
        internal static double RemovalDepth(bool open, double freeboard, double minimumDepth)
        {
            if (!open || double.IsNaN(freeboard) || double.IsInfinity(freeboard) || double.IsNaN(minimumDepth) || double.IsInfinity(minimumDepth) || minimumDepth < 0.25) return 0;
            var excess = Math.Max(0, CloseFreeboard - freeboard);
            // A small pulse on each NEW readback, tapered near the closing level.
            return Math.Min(MaximumDepthPerReading, Math.Min(excess * 0.1, minimumDepth * 0.02));
        }
        internal static int DisplayBucket(double? freeboard)
        {
            if (!freeboard.HasValue || double.IsNaN(freeboard.Value) || double.IsInfinity(freeboard.Value)) return 253;
            if (freeboard < -5) return 251;
            if (freeboard > 20) return 252;
            return (int)Math.Round((freeboard.Value + 5) * 10, MidpointRounding.AwayFromZero);
        }
        internal static string StatusKey(SpillwayStatus status, int bucket) => "MunRiverPower.Spillway.Status." + (int)status + "." + bucket;
        internal static string StatusText(bool thai, SpillwayStatus status, int bucket)
        {
            var distance = bucket == 253 ? (thai ? "รออ่านระดับน้ำ" : "Waiting for water levels")
                : bucket == 251 ? (thai ? "น้ำสูงกว่าสันเกิน 5 ม." : "Water more than 5 m above crest")
                : bucket == 252 ? (thai ? "เหลือก่อนน้ำล้นมากกว่า 20 ม." : "Freeboard greater than 20 m")
                : (thai ? "ระยะก่อนน้ำล้น: " : "Freeboard: ") + (bucket / 10.0 - 5).ToString("F1", CultureInfo.InvariantCulture) + (thai ? " ม." : " m");
            var names = thai
                ? new[] { "ยังไม่ติดตั้ง", "ปิดอัตโนมัติ", "เปิดระบาย", "ปิดใช้งาน", "ไม่มีจุดน้ำที่เหมาะสม", "ระบบน้ำรุ่นนี้ยังไม่รองรับ", "รอข้อมูลน้ำรอบใหม่", "ท้ายน้ำสูง—หยุดระบาย" }
                : new[] { "Not installed", "Closed automatically", "Releasing water", "Disabled", "No suitable wet cross-section", "Unsupported water engine", "Waiting for fresh water data", "Downstream too high—release stopped" };
            return distance + " · Automatic Spillway: " + names[(int)status] + (thai ? " · เปิดที่ 0.5 ม.; ปิดที่ 1.0 ม." : " · Open at 0.5 m; close at 1.0 m");
        }
    }
}
