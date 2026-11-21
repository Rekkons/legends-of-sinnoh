using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 좌석 색 [가정 B + 안전장치, 04 §3-1·§6 Q2, 결정 22 Q2 손대지 않음 — 사용자 확인은 M4-3(plan §5 #4)]:
    /// 좌석 색 = 고향 마을 색(원작 TTS 토큰 틴트를 화면용으로 다듬은 값, 목업 `mockup/r2/r2.js` VILLAGE_B). 고향을 아직 안 골랐으면 회색.
    /// 안전장치: 좌석 번호를 늘 같이 쓰고(핀), 고리는 두 겹(바깥 어두운 선 + 안쪽 흰 선), 번호 글자는 흰/검정 중 대비가 큰 쪽.
    /// </summary>
    public static class SeatColors
    {
        /// <summary>고리 바깥 선·핀 테두리·종이 바깥 선(#10141F).</summary>
        public static readonly Color32 Edge = new Color32(0x10, 0x14, 0x1F, 0xFF);
        /// <summary>말 그림 종이 테두리(목업 paper 필터 #F6F2E6).</summary>
        public static readonly Color32 Paper = new Color32(0xF6, 0xF2, 0xE6, 0xFF);
        /// <summary>고향을 아직 안 고른 좌석(로비 회색) [가정 M4-2d].</summary>
        public static readonly Color32 Unassigned = new Color32(0x8A, 0x93, 0xA6, 0xFF);

        /// <summary>고향 마을 키(fixtures `village.*`) → 좌석 색. 모르는 키는 Unassigned.</summary>
        public static Color32 ForVillage(string villageKey)
        {
            switch (villageKey)
            {
                case "village.sinnome": return Hex(0xFF4FC8);           // 꽃향기마을
                case "village.stark": return Hex(0xCF1F1F);             // 무쇠시티
                case "village.nesburgh": return Hex(0xF08A00);          // 장막시티
                case "village.celestic": return Hex(0x178C48);          // 봉신마을
                case "village.eterna": return Hex(0x8A2BE2);            // 영원시티
                case "village.snowpoint": return Hex(0xEEF2F8);         // 선단시티
                case "village.canalave": return Hex(0x4DA3FF);          // 운하시티(원색 #0000C8은 어두운 바탕 대비 1.59라 밝힘)
                case "village.sunyshore": return Hex(0xF2C200);         // 물가시티
                case "village.ultra_megalopolis": return Hex(0x14D6B0); // 울트라메가로폴리스
                default: return Unassigned;
            }
        }

        /// <summary>색 위 번호 글자: 상대 휘도 &gt; 0.179면 어두운 글자, 아니면 흰 글자(목업 ink, WCAG 대비가 큰 쪽).</summary>
        public static Color32 Ink(Color32 c)
        {
            double l = 0.2126 * Lin(c.r) + 0.7152 * Lin(c.g) + 0.0722 * Lin(c.b);
            return l > 0.179 ? Edge : new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        }

        static double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : System.Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        static Color32 Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xFF);
    }
}
