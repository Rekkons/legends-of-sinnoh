using System;
using System.Collections.Generic;
using Sinnoh.Rules;

namespace Sinnoh.Data
{
    /// <summary>판 위 피규어 하나의 자리(화면용, 월드 유닛). Slot = 칸 안 자리(-1 = 가운데, 0~5 = FigureLayout.SlotOffset), Y = 칸 윗면.</summary>
    public sealed class FigureSpot
    {
        public readonly string Key;
        public readonly int Cell;
        public readonly int Slot;
        public readonly decimal X;
        public readonly decimal Y;
        public readonly decimal Z;

        public FigureSpot(string key, int cell, int slot, decimal x, decimal y, decimal z)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Cell = cell;
            Slot = slot;
            X = x;
            Y = y;
            Z = z;
        }
    }

    /// <summary>
    /// 피규어를 세울 칸(M4-2c, d3-3 §2-2, 엔진 무관 — dotnet 시험이 실제 데이터로 본다). 입력은 figure_list 키·group(fixtures kind) 쌍.
    /// - LegendarySite·LegendaryShrine: 그 종을 `legendary`에 가진 같은 종류 고정물 칸(전설 장소 12 = 11 + 신오신전 실버디, 전설 사당 7칸에 10 — 시작의 사당 3신·달빛공터 2).
    /// - Taxi: 택시 고정물 칸(마을 칸과 겹친다). UltraBurst: 1페이즈(state 1)만 깨어진 세계 가운데 칸(원작 `_expansion_setup`: 받침 위). 다음 페이즈 폼은 같은 자리에서 바꿔 선다 → 여기서는 자리 없음.
    /// - UltraBeast: 원작 `setup_items`처럼 지형·기후가 맞는 본맵 야생 칸(고정물·다른 피규어 없음, 이미 쓴 지역은 건너뜀)을 섞어 고른다. 섞기는 시드 고정 PCG32 [가정 M4-2c: 실제 셋업은 M6 규칙 엔진].
    /// - 그 밖(오리진 기라티나 등 판에 서지 않는 폼): 자리 없음.
    /// 한 칸에 둘 이상이면 칸 안 자리로 벌린다(04 §3-1): n개 → 자리 i·6/n.
    /// </summary>
    public static class FigureLayout
    {
        /// <summary>칸 안 자리 반지름 [가정 M4-2c]. 안접 반지름 0.9526의 약 절반 — 말(M4-2d)도 같은 6자리를 쓴다.</summary>
        public const decimal SlotRadius = 0.5m;

        /// <summary>UB 자리 섞기 시드 [가정 M4-2c] — 시험 판이 늘 같은 그림이 되게.</summary>
        public const ulong DefaultUbSeed = 20261108UL;

        const decimal HalfSqrt3 = 0.8660254037844386m;

        /// <summary>칸 안 자리 0~5 = flat-top 꼭짓점 방향(0 = +x, 반시계 60°씩) × SlotRadius. -1 = 가운데.</summary>
        public static void SlotOffset(int slot, out decimal dx, out decimal dz)
        {
            decimal c, s;
            switch (slot)
            {
                case -1: dx = 0; dz = 0; return;
                case 0: c = 1; s = 0; break;
                case 1: c = 0.5m; s = HalfSqrt3; break;
                case 2: c = -0.5m; s = HalfSqrt3; break;
                case 3: c = -1; s = 0; break;
                case 4: c = -0.5m; s = -HalfSqrt3; break;
                case 5: c = 0.5m; s = -HalfSqrt3; break;
                default: throw new ArgumentOutOfRangeException(nameof(slot), slot, "-1~5");
            }
            dx = c * SlotRadius;
            dz = s * SlotRadius;
        }

        /// <summary>한 칸에 n개일 때 i번째(0부터)의 자리. 1개면 가운데, 2개면 0·3(좌우), 3개면 0·2·4.</summary>
        public static int SlotFor(int i, int n)
        {
            if (n < 1 || n > 6 || i < 0 || i >= n) throw new ArgumentOutOfRangeException(nameof(i), $"{i}/{n}");
            return n == 1 ? -1 : i * 6 / n;
        }

        /// <summary>UB 서식지(원작 세이브 UB 토큰 태그 [확인 save 8cccdb·252fa1·67ce82·e5b898·6dd62b·53b026·7b50f2·03bad9·9829fd]). pokemon.json에는 없다(UB 카드 climate = Space).</summary>
        public static bool UltraBeastHabitat(string key, out Biome biome, out Climate climate) =>
            UltraBurstSetup.Habitat(key, out biome, out climate); // 표는 Rules에 있다(셋업의 UB 시작 자리가 같은 표를 읽는다 — 참조 방향 Rules ← Data, M6 「UB 셋업·맵」)

        /// <summary>keys·groups(같은 길이, figure_list 순서) → 판에 서는 것만 자리. 결과 순서 = 입력 순서.</summary>
        public static List<FigureSpot> Place(Board board, IReadOnlyList<CellLook> looks, FixtureDb fixtures, CardDb cards,
            IReadOnlyList<string> keys, IReadOnlyList<string> groups, ulong ubSeed = DefaultUbSeed)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (looks == null || looks.Count != board.Count) throw new ArgumentException("looks는 칸마다 하나", nameof(looks));
            if (fixtures == null) throw new ArgumentNullException(nameof(fixtures));
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            if (keys == null || groups == null || keys.Count != groups.Count) throw new ArgumentException("keys·groups 길이가 다르다");

            var cellOf = new int[keys.Count];
            var taken = new bool[board.Count];
            foreach (var f in fixtures.Fixtures) taken[f.Cell] = true;

            for (int i = 0; i < keys.Count; i++)
            {
                cellOf[i] = -1;
                switch (groups[i])
                {
                    case "LegendarySite": cellOf[i] = LegendCell(fixtures, cards, keys[i], FixtureKind.LegendarySite); break;
                    case "LegendaryShrine": cellOf[i] = LegendCell(fixtures, cards, keys[i], FixtureKind.LegendaryShrine); break;
                    case "Taxi":
                        foreach (var f in fixtures.Fixtures)
                            if (f.Kind == FixtureKind.Taxi) { cellOf[i] = f.Cell; break; }
                        break;
                    case "UltraBurst":
                        if (cards.PokemonIds.TryIndexOf(keys[i], out int p) && cards.Pokemon[p].State == 1) cellOf[i] = DistortionCenter(board, looks);
                        break;
                    case "UltraBeast": break; // 아래에서 고정 칸을 다 잡은 뒤
                    default: throw new ArgumentException($"{keys[i]}: 모르는 group {groups[i]}", nameof(groups));
                }
            }
            for (int i = 0; i < keys.Count; i++) if (cellOf[i] >= 0) taken[cellOf[i]] = true;
            PlaceUltraBeasts(board, keys, groups, cellOf, taken, ubSeed);

            // 칸마다 몇 개인지 → 칸 안 자리
            var count = new int[board.Count];
            for (int i = 0; i < keys.Count; i++) if (cellOf[i] >= 0) count[cellOf[i]]++;
            var used = new int[board.Count];
            var spots = new List<FigureSpot>();
            for (int i = 0; i < keys.Count; i++)
            {
                int c = cellOf[i];
                if (c < 0) continue;
                int slot = SlotFor(used[c]++, count[c]);
                SlotOffset(slot, out decimal dx, out decimal dz);
                spots.Add(new FigureSpot(keys[i], c, slot, looks[c].X + dx, BoardLayout.TopY(board[c]), looks[c].Z + dz));
            }
            return spots;
        }

        static int LegendCell(FixtureDb fixtures, CardDb cards, string key, FixtureKind kind)
        {
            if (!cards.PokemonIds.TryIndexOf(key, out int p)) return -1;
            foreach (var f in fixtures.Fixtures)
                if (f.Kind == kind)
                    foreach (int l in f.Legendary)
                        if (l == p) return f.Cell;
            return -1;
        }

        /// <summary>깨어진 세계 칸 중 그 칸들의 무게중심에 가장 가까운 칸(같으면 앞 색인).</summary>
        public static int DistortionCenter(Board board, IReadOnlyList<CellLook> looks)
        {
            decimal sx = 0, sz = 0;
            int n = 0;
            for (int i = 0; i < board.Count; i++)
                if (board[i].DistortionWorld) { sx += looks[i].X; sz += looks[i].Z; n++; }
            if (n == 0) return -1;
            sx /= n;
            sz /= n;
            int best = -1;
            decimal bestD = 0;
            for (int i = 0; i < board.Count; i++)
            {
                if (!board[i].DistortionWorld) continue;
                decimal dx = looks[i].X - sx, dz = looks[i].Z - sz, d = dx * dx + dz * dz;
                if (best < 0 || d < bestD) { best = i; bestD = d; }
            }
            return best;
        }

        // 원작 _expansion_setup → setup_items: UB 순서를 섞고, UB마다 맞는 야생 칸을 섞어 처음 「안전한」 칸에 놓는다.
        // 안전 = 본맵·고정물 없음·다른 피규어 없음·이번 셋업에서 아직 안 쓴 지역. 한 바퀴 다 봐도 없을 때만 지역 기록을 비우고 다시 본다
        // (rulings G12·B3 — 숨겨진 장소 배치 SetupFlow.PlaceHiddenSpots와 같은 고침).
        static void PlaceUltraBeasts(Board board, IReadOnlyList<string> keys, IReadOnlyList<string> groups, int[] cellOf, bool[] taken, ulong seed)
        {
            var rng = new Pcg32(seed);
            var order = new List<int>();
            for (int i = 0; i < keys.Count; i++) if (groups[i] == "UltraBeast") order.Add(i);
            Piles.Shuffle(order, rng);
            var claimed = new List<string>();
            foreach (int i in order)
            {
                if (!UltraBeastHabitat(keys[i], out var biome, out var climate)) continue;
                var cands = new List<int>();
                for (int c = 0; c < board.Count; c++)
                {
                    var cell = board[c];
                    if (cell.Wild && !cell.DistortionWorld && cell.Biome == biome && cell.Climate == climate) cands.Add(c);
                }
                Piles.Shuffle(cands, rng);
                for (int pass = 0; pass < 2 && cellOf[i] < 0; pass++)
                {
                    foreach (int c in cands)
                        if (!taken[c] && !claimed.Contains(board[c].Region)) { cellOf[i] = c; break; }
                    if (cellOf[i] < 0) claimed.Clear();
                }
                if (cellOf[i] < 0) continue;
                taken[cellOf[i]] = true;
                claimed.Add(board[cellOf[i]].Region);
            }
        }
    }
}
