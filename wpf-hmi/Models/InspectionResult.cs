using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VisiPickHMI.Models
{
    public class InspectionResult
    {
        [Key]
        public int Id { get; set; }
        public string Timestamp { get; set; } = DateTime.Now.ToString("O");
        public string ComponentType { get; set; } = string.Empty;
        public string Class { get; set; } = string.Empty;
        public string DefectCode { get; set; } = "PASS";
        public string Result { get; set; } = "양품";
        public double Confidence { get; set; }
        public int CycleTimeMs { get; set; }
        public int GateUsed { get; set; }

        /// <summary>
        /// 불량 유형 한글 표시 (파손/핀휨/—)
        /// DB 컬럼이 아닌 표시 전용 프로퍼티
        /// </summary>
        [NotMapped]
        public string DefectTypeDisplay
        {
            get
            {
                if (DefectCode == "PASS" || DefectCode == "NONE" || string.IsNullOrEmpty(DefectCode))
                    return "—";
                switch (DefectCode.ToUpper())
                {
                    case "BENT_PIN":
                    case "POLARITY":
                        return "핀휨";
                    case "BROKEN":
                        return "파손";
                    case "UNKNOWN":
                        return "미상";
                    default:
                        return "파손";
                }
            }
        }

        /// <summary>
        /// Result에서 등급 표시 (Pass / Defect)
        /// </summary>
        [NotMapped]
        public string ResultGradeDisplay
        {
            get
            {
                switch (Result?.ToUpper())
                {
                    case "DEFECT": case "불량": return "Defect";
                    default: return "Pass";
                }
            }
        }

        /// <summary>
        /// 결과 등급에 따른 색상 Hex (Defect=빨강, Pass=초록)
        /// </summary>
        [NotMapped]
        public string ResultColorHex
        {
            get
            {
                switch (Result?.ToUpper())
                {
                    case "DEFECT": case "불량": return "#EF5350";
                    default: return "#66BB6A";
                }
            }
        }
    }
}