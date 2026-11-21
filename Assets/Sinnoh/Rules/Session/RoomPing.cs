using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sinnoh.Rules.Session
{
    /// <summary>
    /// 핑(결정 97 · 102 ①, 02 §4-8 「소통」): 방 판에서 보드의 누른 자리나 보드 밖 공개 대상에 표시를 찍어 다른 좌석에게 보인다. 종류 넷(여기 봐 · ? · 좋아 · 조심 — <see cref="SocialCodes.Ping"/>).
    /// 방 층 메시지다(<see cref="SendSocial"/> → <see cref="SocialMsg"/>, RoomSocial.cs) — 게임 상태 · 세이브 · 리플레이에 넣지 않는다.
    /// </summary>
    public static class PingRules
    {
        /// <summary>화면에 보이는 시간(ms).</summary>
        public const long ShowMs = 4000;
        /// <summary>좌석당 빈도 제한(<see cref="SocialRate.Of"/>의 Ping 줄과 같은 값): <see cref="WindowMs"/> 안에 <see cref="Burst"/>번까지(넘으면 방이 버린다 — 알림 없음).</summary>
        public const int Burst = 3;
        public const long WindowMs = 5000;
        /// <summary>누른 자리의 단위(천분율)와 칸 가운데에서의 한도: 보드 칸 = 칸 반지름 대비 ±<see cref="CellMax"/>(칸 밖 여백을 눌러도 가장 가까운 칸에서 두 칸 안), 그 밖 = 대상 사각형 안 0~<see cref="Unit"/>.</summary>
        public const int Unit = 1000, CellMax = 2000;

        public static int Clamp(PingAt at, int v) => at == PingAt.Cell ? Math.Max(-CellMax, Math.Min(CellMax, v)) : Math.Max(0, Math.Min(Unit, v));
    }

    /// <summary>핑 대상의 갈래: 보드 칸 · 좌석(좌석 칩 — 숨은 것(남의 손패 등)은 여기까지만) · 보드 밖 공개 대상(<see cref="PingSpots"/> 허용 목록의 한 줄).</summary>
    public enum PingAt { Cell = 0, Seat = 1, Spot = 2 }

    /// <summary>
    /// 핑 대상(전선 위): <see cref="At"/> = Cell이면 N = 칸, Seat이면 Seat = 좌석, Spot이면 N = <see cref="PingSpots.Rows"/>의 줄 번호 · K = 그 줄의 번호(없으면 −1) · Seat = 그 요소의 주인 좌석(없으면 −1).
    /// Pos = 누른 자리가 실렸나(false = 대상 가운데). X·Y: 칸 = 칸 윗면 가운데에서 보드 평면(월드 X · Z) 위 위치 — 칸 반지름 대비 천분율, 그 밖 = 대상 사각형 안 상대 위치(왼쪽 위 0 ~ 오른쪽 아래 1000).
    /// 요소 이름 · 카드 번호 같은 글은 싣지 않는다 — 방장이 <see cref="PingSpots.Norm"/>으로 허용된 칸만 새로 만들어 넘긴다.
    /// </summary>
    public sealed class PingTarget
    {
        public PingAt At;
        public int Seat = -1;
        public int N = -1;
        public int K = -1;
        public bool Pos;
        public int X, Y;

        public PingTarget Copy() => (PingTarget)MemberwiseClone();

        public static PingTarget Cell(int cell) => new PingTarget { At = PingAt.Cell, N = cell };
        public static PingTarget OfSeat(int seat) => new PingTarget { At = PingAt.Seat, Seat = seat };
        public static PingTarget Spot(int row, int k = -1, int seat = -1) => new PingTarget { At = PingAt.Spot, N = row, K = k, Seat = seat };

        /// <summary>누른 자리를 실은 사본(범위는 <see cref="PingRules.Clamp"/>).</summary>
        public PingTarget At2(int x, int y)
        {
            var c = Copy();
            c.Pos = true;
            c.X = PingRules.Clamp(At, x);
            c.Y = PingRules.Clamp(At, y);
            return c;
        }

        /// <summary>같은 곳을 가리키는가(누른 자리는 보지 않는다).</summary>
        public bool Same(PingTarget o) => o != null && At == o.At && Seat == o.Seat && N == o.N && K == o.K;
    }

    /// <summary>
    /// 허용 목록 한 줄의 범위: Shared = 모든 화면에 같은 이름으로 있는 공개 요소 · Mine = 보낸 좌석 자신의 것(아래 판 — 받는 쪽은 그 좌석의 칩) ·
    /// Owned = 어느 좌석의 것을 보이는 창(정비 판 · 트레이너 카드 — 받는 쪽이 같은 좌석의 창을 열고 있을 때만 그 요소, 아니면 좌석 칩) ·
    /// Side = 배틀의 쪽(K = 0·1 — 화면마다 「나」·「상대」가 다르다) · Log = 기록 줄(K = 행동 번호).
    /// </summary>
    public enum SpotScope { Shared, Mine, Owned, Side, Log }

    public sealed class PingSpotRow
    {
        /// <summary>요소 이름(Indexed면 머리 — 뒤에 번호가 붙는다).</summary>
        public readonly string Name;
        public readonly SpotScope Scope;
        public readonly bool Indexed;
        public readonly int Min, Max;
        /// <summary>받는 화면에 그 요소가 없을 때 대신 찾을 요소 이름(없으면 null — 주인 좌석이 있으면 그 칩).</summary>
        public readonly string Fallback;

        public PingSpotRow(string name, SpotScope scope = SpotScope.Shared, string fallback = null, int min = 0, int max = -1)
        {
            Name = name;
            Scope = scope;
            Fallback = fallback;
            Indexed = max >= min;
            Min = min;
            Max = max;
        }
    }

    /// <summary>
    /// 보드 밖 「공개 대상 허용 목록」(결정 102 ① — 받는 화면에서 같은 요소를 찾을 수 있는 것만). 줄 번호가 전선 위의 값이라 줄을 끼워 넣지 않고 끝에 더한다(더하면 <see cref="Protocol.Version"/>을 올린다).
    /// 목록에 없는 요소(남의 손패 · 비밀 선택 · 뒷면 카드 · 고르는 중인 창)는 대상이 아니다 — 내 손패 창은 좌석까지만(<see cref="ToSeat"/>).
    /// </summary>
    public static class PingSpots
    {
        public const int LogRow = 11;

        public static readonly PingSpotRow[] Rows =
        {
            new PingSpotRow("board-hud-turn"),                                          // 0 위 띠: 턴
            new PingSpotRow("board-hud-round"),                                         // 1 라운드
            new PingSpotRow("board-hud-time"),                                          // 2 시간대
            new PingSpotRow("board-hud-weather"),                                       // 3 날씨
            new PingSpotRow("board-hud-act"),                                           // 4 막
            new PingSpotRow("board-hud-ub"),                                            // 5 균열 칩
            new PingSpotRow("board-hud-ub-prism"),                                      // 6 프리즘 칩
            new PingSpotRow("board-hud-ub-bless"),                                      // 7 축복 칩
            new PingSpotRow("board-hud-mon-", SpotScope.Mine, null, 0, 5),              // 8 아래 판: 내 파티 칸
            new PingSpotRow("board-hud-party", SpotScope.Mine),                         // 9 아래 판: 파티 줄
            new PingSpotRow("board-hud-actions", SpotScope.Mine),                       // 10 아래 판: 행동 줄
            new PingSpotRow("log-line-", SpotScope.Log, "board-hud-log", 0, int.MaxValue),   // 11 기록 줄(행동 번호)
            new PingSpotRow("board-hud-log"),                                           // 12 기록 판
            new PingSpotRow("act-card-panel"),                                          // 13 막 카드
            new PingSpotRow("encounter-card-", SpotScope.Shared, "encounter-panel", 1, 9),   // 14 조우 카드
            new PingSpotRow("board-trainer-panel", SpotScope.Owned),                    // 15 트레이너 카드
            new PingSpotRow("board-party-mon-", SpotScope.Owned, null, 0, 5),           // 16 정비 판의 포켓몬 카드
            new PingSpotRow("battle-info-", SpotScope.Side, "battle-info"),             // 17 배틀 정보 기둥의 한쪽(포켓몬 · 기술)
            new PingSpotRow("battle-ds", SpotScope.Side),                               // 18 배틀 화면(DS)
            new PingSpotRow("battle-move-", SpotScope.Shared, "battle-dock", 1, 8),     // 19 배틀 독: 기술 줄
            new PingSpotRow("battle-roll-face-", SpotScope.Shared, "battle-dock", 1, 12),   // 20 주사위 눈
            new PingSpotRow("battle-roll", SpotScope.Shared, "battle-dock"),            // 21 주사위 판
            new PingSpotRow("battle-calc", SpotScope.Shared, "battle-dock"),            // 22 데미지 계산
            new PingSpotRow("battle-turn", SpotScope.Shared, "battle-dock"),            // 23 차례 칸
            new PingSpotRow("battle-log"),                                              // 24 배틀 기록
            new PingSpotRow("battle-result-panel"),                                     // 25 배틀 결과 창
        };

        /// <summary>숨은 것이 든 내 창 · 판: 요소가 아니라 내 좌석까지만 가리킨다(결정 102).</summary>
        public static readonly string[] ToSeat = { "board-fate-panel", "board-hud-dock" };

        /// <summary>요소 이름 → 줄 번호(없으면 −1). Indexed 줄은 머리 뒤가 숫자뿐일 때만(「board-hud-mon-2-name」은 아니다), Side 줄의 번호는 화면이 채운다(k = −1).</summary>
        public static int Find(string name, out int k)
        {
            k = -1;
            if (string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < Rows.Length; i++)
            {
                var r = Rows[i];
                if (r.Scope == SpotScope.Side && r.Name.EndsWith("-", StringComparison.Ordinal))
                {
                    if (name == r.Name + "me" || name == r.Name + "foe") return i;
                    continue;
                }
                if (!r.Indexed)
                {
                    if (name == r.Name) return i;
                    continue;
                }
                if (name.Length <= r.Name.Length || !name.StartsWith(r.Name, StringComparison.Ordinal)) continue;
                if (!int.TryParse(name.Substring(r.Name.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < r.Min || n > r.Max) continue;
                k = n;
                return i;
            }
            return -1;
        }

        /// <summary>줄 + 번호 → 요소 이름(Side 줄 가운데 「-me」·「-foe」로 갈리는 것은 머리만 — 화면이 쪽을 붙인다).</summary>
        public static string NameOf(int row, int k)
        {
            var r = Rows[row];
            return r.Indexed ? r.Name + k.ToString(CultureInfo.InvariantCulture) : r.Name;
        }

        /// <summary>
        /// 방장: 받은 대상을 허용된 칸만으로 새로 만든다(틀리면 null — 버린다). from = 보낸 좌석, seats = 좌석 수, cells = 보드 칸 수, nextSeq = 지금까지의 행동 수.
        /// Mine 줄의 주인은 늘 보낸 좌석이고, 누른 자리는 범위 안으로 자른다.
        /// </summary>
        public static PingTarget Norm(PingTarget t, int from, int seats, int cells, int nextSeq)
        {
            if (t == null) return null;
            PingTarget o;
            switch (t.At)
            {
                case PingAt.Cell:
                    if (t.N < 0 || t.N >= cells) return null;
                    o = PingTarget.Cell(t.N);
                    break;
                case PingAt.Seat:
                    if (t.Seat < 0 || t.Seat >= seats) return null;
                    o = PingTarget.OfSeat(t.Seat);
                    break;
                case PingAt.Spot:
                {
                    if (t.N < 0 || t.N >= Rows.Length) return null;
                    var r = Rows[t.N];
                    int k = -1, seat = -1;
                    if (r.Scope == SpotScope.Side)
                    {
                        if (t.K != 0 && t.K != 1) return null;
                        k = t.K;
                    }
                    else if (r.Scope == SpotScope.Log)
                    {
                        if (t.K < 0 || t.K >= nextSeq) return null;
                        k = t.K;
                    }
                    else if (r.Indexed)
                    {
                        if (t.K < r.Min || t.K > r.Max) return null;
                        k = t.K;
                    }
                    if (r.Scope == SpotScope.Mine) seat = from;
                    else if (r.Scope == SpotScope.Owned)
                    {
                        if (t.Seat < 0 || t.Seat >= seats) return null;
                        seat = t.Seat;
                    }
                    o = PingTarget.Spot(t.N, k, seat);
                    break;
                }
                default:
                    return null;
            }
            return t.Pos ? o.At2(t.X, t.Y) : o;
        }
    }
}
