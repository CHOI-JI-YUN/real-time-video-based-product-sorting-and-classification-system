namespace VisiPickHMI.Models
{
    /// <summary>
    /// visipick/inspection MQTT 페이로드 매핑 (인수인계 문서 기준)
    ///
    /// {
    ///   "part_type":          "IC칩",
    ///   "classification":     "NEEDED",          // NEEDED / DUPLICATE / DEFECT / UNCERTAIN
    ///   "defect_code":        "NONE",            // NONE / BENT_PIN / BROKEN / UNKNOWN
    ///   "confidence":         0.96,
    ///   "gate_action":        "PASS_THROUGH",    // PASS_THROUGH / GATE1_PUSH / GATE2_PUSH
    ///   "cycle_time_ms":      120,
    ///   "recipe_session_id":  105,
    ///   "timestamp":          "2026-06-04T10:30:00"
    /// }
    /// </summary>
    public class ClassificationMessage
    {
        // ── 새 필드 (인수인계 기준) ──
        public string PartType { get; set; } = string.Empty;          // part_type
        public string Classification { get; set; } = "NEEDED";        // NEEDED / DUPLICATE / DEFECT / UNCERTAIN
        public string DefectCode { get; set; } = "NONE";              // NONE / BENT_PIN / BROKEN / UNKNOWN
        public double Confidence { get; set; }
        public string GateAction { get; set; } = "PASS_THROUGH";      // PASS_THROUGH / GATE1_PUSH / GATE2_PUSH
        public int CycleTimeMs { get; set; }
        public int RecipeSessionId { get; set; }
        public string Timestamp { get; set; } = string.Empty;

        // ── 기존 코드 호환 헬퍼 (ViewModel에서 사용) ──

        /// <summary>part_type → Name (기존 호환)</summary>
        public string Name => PartType;

        /// <summary>
        /// part_type → 클래스 매핑 (UI 4클래스 기준)
        ///   IC칩 → A · 터미널블록 → B · 방열판 → C · 커패시터 → D
        /// part_type을 모를 때만 gate_action으로 폴백
        ///   (GATE1_PUSH → A, GATE2_PUSH → B, PASS_THROUGH → C)
        /// </summary>
        public string Class => PartType switch
        {
            "IC칩" => "A",
            "터미널블록" => "B",
            "방열판" => "C",
            "커패시터" => "D",
            _ => GateAction switch
            {
                "GATE1_PUSH" => "A",
                "GATE2_PUSH" => "B",
                _ => "C"   // PASS_THROUGH 포함
            }
        };

        /// <summary>
        /// classification → Result 매핑 (기존 UpdateClassificationResult 호환)
        ///   NEEDED    → PASS
        ///   DEFECT    → DEFECT
        ///   DUPLICATE → DUPLICATE
        ///   UNCERTAIN → UNCERTAIN
        /// </summary>
        public string Result => Classification switch
        {
            "NEEDED" => "PASS",
            "DEFECT" => "DEFECT",
            "DUPLICATE" => "DUPLICATE",
            "UNCERTAIN" => "UNCERTAIN",
            _ => Classification
        };

        /// <summary>gate_action → GateUsed 번호</summary>
        public int GateUsed => GateAction switch
        {
            "GATE1_PUSH" => 1,
            "GATE2_PUSH" => 2,
            _ => 0   // PASS_THROUGH = 게이트 미사용
        };
    }
}