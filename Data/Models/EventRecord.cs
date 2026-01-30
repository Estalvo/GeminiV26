using System;

namespace GeminiV26.Data.Models
{
    public class EventRecord
    {
        public DateTime EventTimestamp { get; set; }
        public string Symbol { get; set; }
        public string EventType { get; set; }
        public long PositionId { get; set; }
        public int? Confidence { get; set; }

        public string Reason { get; set; }

        // 🆕 TVM-hez
        public string Extra { get; set; }     // pl. "M5=true,M1=false,SR=true"
        public double? RValue { get; set; }   // R az exit pillanatában
    }

}
