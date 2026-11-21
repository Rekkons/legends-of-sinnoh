using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Fx
{
    /// <summary>
    /// 도트 이펙트 시트 하나(결정 69 — Gen <c>Stage/Fx/fx.json</c>의 한 줄, <c>Tools/assets/lib/pixelfx.mjs</c>가 공용 pixelfx 저장소에서 렌더해 만든다):
    /// 프레임 <see cref="Fw"/>×<see cref="Fh"/>가 가로로 <see cref="Frames"/>장, 프레임 하나가 <see cref="Ms"/>ms. 고정점(<see cref="Ox"/>·<see cref="Oy"/>) = 화면이 준 자리에 놓이는 프레임 안 칸(프레임 밖일 수 있다).
    /// 프레임은 시작한 뒤의 시간만으로 고른다 — 같은 시각이면 같은 그림이다(연출 대기열의 시각을 넣으면 「빠름」·「끄기」를 따라온다).
    /// </summary>
    public sealed class FxSheet
    {
        public const string TableKey = "Stage/Fx/fx.json";

        public string Key, File;
        public int Fw, Fh, Frames, Ms, Ox, Oy;
        public bool Loop;

        static readonly Regex Row = new Regex("\"key\":\"([a-zA-Z0-9_]+)\",\"file\":\"(Fx/[^\"]+\\.png)\".*\"fw\":(\\d+),\"fh\":(\\d+),\"frames\":(\\d+),\"ms\":(\\d+),\"ox\":(-?\\d+),\"oy\":(-?\\d+),\"loop\":(true|false)");

        /// <summary>한 번 도는 길이(ms).</summary>
        public int TotalMs => Frames * Ms;

        /// <summary>시작한 뒤 ms의 프레임(0부터). 시작 전이거나 다 돈 뒤(한 번 도는 시트)면 −1 = 그리지 않는다. 도는 시트는 끝없이 돈다.</summary>
        public int FrameAt(long ms)
        {
            if (ms < 0 || Frames <= 0 || Ms <= 0) return -1;
            long f = ms / Ms;
            if (Loop) return (int)(f % Frames);
            return f < Frames ? (int)f : -1;
        }

        /// <summary>표의 한 줄(행 하나 = 한 줄)을 읽는다. 시트 행이 아니면 null.</summary>
        public static FxSheet Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            var m = Row.Match(line);
            if (!m.Success) return null;
            int N(int g) => int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            return new FxSheet { Key = m.Groups[1].Value, File = m.Groups[2].Value, Fw = N(3), Fh = N(4), Frames = N(5), Ms = N(6), Ox = N(7), Oy = N(8), Loop = m.Groups[9].Value == "true" };
        }

        public static List<FxSheet> ParseAll(string table)
        {
            var list = new List<FxSheet>();
            if (table == null) return list;
            foreach (var line in table.Split('\n'))
            {
                var s = Parse(line);
                if (s != null) list.Add(s);
            }
            return list;
        }
    }

    /// <summary>그릴 이펙트 하나: 시트 키 · 시작한 뒤의 시간(ms) · 고정점이 놓일 자리(DS 층은 DS px, 보드는 화면이 정한다).</summary>
    public struct FxStamp
    {
        public string Key;
        public int Ms;
        public int X, Y;
    }

    /// <summary>
    /// 시트 키와 자리별 박자(결정 69). 박자(<c>PlayTiming</c>)는 그대로고 그림만 더한다 — 모두 이미 그리는 행 안의 한 순간에서 시작한다:
    /// 기술 맞음(61행 체력이 깎이기 시작, 색 = 기술 타입) · 기절(65행 도트가 다 내려갈 무렵 발 자리의 먼지) · 볼 열림 빛(66행 출전 — 볼에서 나오는 쪽만, 68행 포획의 열림과 튀어나옴, 3행 스타팅) ·
    /// 회복 반짝임(8·17행 칸이 반짝일 때) · 착지 먼지(2행 말이 내려앉음 · 16행 내려앉음) · 숨겨진 장소 공개(25행 앞면으로 바뀔 때).
    /// 결정 70 ⑬으로 뺀 자리: 배틀 시작(53행 인트로)의 볼 열림 빛 · 포획 성공 반짝임(68행 판정 — 원래 별 셋만) · 걷기 끝(11행)의 착지 먼지.
    /// </summary>
    public static class SheetBeats
    {
        public const string Open = "open", Star = "star", Heal = "heal", Dust = "dust";

        /// <summary>기술 맞음의 시트: 상대 자리의 포켓몬이 맞으면 오른쪽으로, 내 자리면 왼쪽으로 튄다. 색 = 기술 타입.</summary>
        public static string Hit(PokemonType type, bool enemy) => (enemy ? "hit_" : "hitl_") + (Enum.IsDefined(typeof(PokemonType), type) ? type : PokemonType.Typeless);

        /// <summary>화면이 읽는 시트 키 전부(Gen 표에 다 있어야 한다 — 에디터 시험이 본다).</summary>
        public static List<string> All()
        {
            var list = new List<string> { Open, Star, Heal, Dust };
            foreach (PokemonType t in Enum.GetValues(typeof(PokemonType)))
            {
                list.Add(Hit(t, true));
                list.Add(Hit(t, false));
            }
            return list;
        }

        /// <summary>DS 화면의 포켓몬 몸 가운데(발 위 px) — 맞는 불꽃·볼 열림 빛의 자리.</summary>
        public const int BodyAbovePx = 22;
        /// <summary>내 자리의 기절 먼지를 발 점에서 올리는 px: 내 발 점(y 146)은 메시지 창 윗줄(144) 아래라 그대로 찍으면 창에 덮인다 — 먼지의 아랫줄이 창 바로 위(143)에 오게 한다 [가정 — plan §5 #62].</summary>
        public const int PlayerDustLiftPx = 3;
        /// <summary>기절(65행): 도트가 다 내려가기 이만큼 전에 먼지가 인다.</summary>
        public const int FaintDustLeadMs = 150;

        /// <summary>고향 마을(2행): 말이 내려앉는 단계 안 시각.</summary>
        public static int VillageLandMs(int total) => total * VillageBeats.LandPermille / 1000;
        /// <summary>숨겨진 장소 공개(25행): 앞면으로 바뀌는 단계 안 시각(뒤집기의 가운데).</summary>
        public static int SpotFaceMs(int total) => total * SpotBeats.FlipPermille / 1000 / 2;
        /// <summary>스타팅(3행): 볼이 열리는 단계 안 시각.</summary>
        public static int StarterOpenMs(int total) => total * StarterBeats.OpenPermille / 1000;
        /// <summary>회복(8·17행): n마리 가운데 i번째 칸의 반짝임이 시작한 뒤의 시간(시작 전이면 음수).</summary>
        public static int HealMs(int ms, int total, int i, int n) => ms - HealBeats.FlashAt(total, i, n);

        /// <summary>
        /// 배틀 단계의 DS 쪽 이펙트(<see cref="BattlePose"/>와 같은 길 — 지금 항목의 단계와 그 안 시각만으로 정한다): 그 포켓몬이 맞음 · 쓰러짐 · 볼에서 나옴(교체·출전만 — 배틀 시작의 인트로에는 없다).
        /// </summary>
        /// <param name="enemy">DS 화면의 상대 자리에 그리나.</param>
        /// <param name="fromBall">그 쪽 포켓몬이 볼에서 나오나(야생은 아니다).</param>
        /// <param name="footX">그 자리의 발판 고정점.</param>
        public static void Battle(FxSequencer seq, int pokemon, bool enemy, bool fromBall, int footX, int footY, List<FxStamp> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            var entry = seq != null ? seq.Current : null;
            if (entry == null || pokemon < 0) return;
            foreach (var s in entry.Steps)
            {
                if (s.Layer != FxLayer.Item || seq.LocalMs < s.StartMs) continue;
                int t = seq.LocalMs - s.StartMs;
                foreach (var e in s.Events)
                    switch (e)
                    {
                        case HpChanged h when s.Row == 612 && h.PokemonId == pokemon && h.After < h.Before && MoveType(entry, pokemon, out var type):
                            into.Add(new FxStamp { Key = Hit(type, enemy), Ms = t, X = footX, Y = footY - BodyAbovePx });
                            break;
                        case Fainted f when f.PokemonId == pokemon:
                            into.Add(new FxStamp { Key = Dust, Ms = t - (BattleBeats.FaintSinkMs - FaintDustLeadMs), X = footX, Y = enemy ? footY : footY - PlayerDustLiftPx });
                            break;
                        case SentIn si when fromBall && si.PokemonId == pokemon && s.Row != FxRows.Intro:
                            into.Add(new FxStamp { Key = Open, Ms = seq.LocalMs - FxReveal.At(entry, si), X = footX, Y = footY - BodyAbovePx });
                            break;
                    }
            }
        }

        /// <summary>그 항목에서 그 포켓몬을 맞힌 기술의 타입(마지막 것). 기술로 맞은 것이 아니면(기권 · 규칙 피해) false — 불꽃이 없다.</summary>
        public static bool MoveType(FxEntry entry, int target, out PokemonType type)
        {
            type = PokemonType.Typeless;
            bool found = false;
            if (entry == null) return false;
            foreach (var e in entry.Events)
                if (e is MoveUsed m && m.TargetId == target)
                {
                    found = true;
                    type = Enum.IsDefined(typeof(PokemonType), m.Type) ? m.Type : PokemonType.Typeless;
                }
            return found;
        }

        /// <summary>
        /// 포획 단계(68행)의 이펙트: 볼이 열릴 때 빛(빨려 들기 시작 · 실패하면 튀어나올 때도). 성공 판정에는 더하지 않는다(결정 70 ⑬ ④ — 원래 별 셋만). t = 단계 안 시각(<see cref="CaptureStage.Pose"/>와 같은 값).
        /// </summary>
        public static void Capture(CaptureAttempt c, int t, int footX, int footY, List<FxStamp> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            if (c == null || t < 0) return;
            int verdict = CaptureStage.VerdictMs(c), topY = footY - EnterBeats.OpenAbovePx, restY = footY - EnterBeats.RestAbovePx;
            into.Add(new FxStamp { Key = Open, Ms = t - EnterBeats.FlyEndMs, X = footX, Y = topY });
            if (!c.Success) into.Add(new FxStamp { Key = Open, Ms = t - verdict, X = footX, Y = restY });
        }
    }
}
