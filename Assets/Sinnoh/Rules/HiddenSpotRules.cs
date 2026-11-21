using System;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 숨겨진 장소 결과 5종(룰북 p.16 · 세이브 토큰 설명, rules-summary §14 · §5.4, rulings A17·A23, M5 「숨겨진 장소·유물·트레이너전 보상」). 공개(A23)한 뒤 <see cref="Resolve"/>가 처리한다.
    /// - 규토리 더미 「Gain 3 Red Apricorns and then discard this token」: 등급 1 색 3개 · 등급 2 색 2개 · 검정 1개(= 4 − 등급), 토큰은 버린다(맵에서 빠짐).
    /// - 야영지 「Allows you to fully restore your party and trade with the Ginkgo Merchants. Token remains where it is found」: 앞면으로 남는다 — 회복·은행상회는
    ///   MapActions.VillagePlace(마을 행동과 같은 턴당 1번), 공중날기는 마을에서만(p.16 「you cannot fly to or from them」).
    /// - 떨어진 별 「Draw 2 Change of Fate cards when revealed. Place this token on an empty Fallen Star spot afterwards」: 운명 2장(손패 넘침 A12) → 모인 별 +1
    ///   (GameState.FallenStars — 4개마다 모든 좌석이 턴 시작에 1장, FateRules.DealStars · 원작 커뮤니티 확인), 토큰은 맵에서 빠진다.
    /// - 유물 11종: 토큰은 맵에서 빠지고 아이템은 가방으로(RelicRules — 지니면 공격력, 가지면 전설 열쇠·천상의 오라 막기).
    /// - 우두머리 「When revealed, you are forced to battle an Alpha Pokémon」(A17): 그 칸의 지형·기후로 강한 덱에서 찾아보기(R1) → 토큰을 지닌 우두머리와 강제 배틀
    ///   (<see cref="BattleFlow.StartAlpha"/> — 체력 +4 · 전설 · 우선권 토큰은 우두머리). 토큰은 그 포켓몬에 붙어 맵에서 빠진다(지거나 쓰러뜨리면 버려지고, 잡으면 남는다).
    ///   자극적인 스튜를 먹은 턴이면 배틀하지 않고 토큰은 앞면으로 남는다(원작 커뮤니티 확인). 앞면 우두머리는 강제가 아니다(원작 커뮤니티 확인) —
    ///   그 칸에서 찾아보기(Search, Target = 토큰)로 싸울 수 있다 [가정 plan §5 #85 ③].
    /// </summary>
    public static class HiddenSpotRules
    {
        public const string Campsite = "hidden_spot.campsite";
        public const string FallenStar = "hidden_spot.fallen_star";

        /// <summary>떨어진 별을 공개하면 받는 운명 카드(p.16 「Draw 2」).</summary>
        public const int StarFate = 2;

        /// <summary>규토리 더미면 그 색과 개수(4 − 등급: 빨강·파랑·노랑 3 · 분홍·초록·하양 2 · 검정 1 — 세이브 토큰 설명). 아니면 false.</summary>
        public static bool Stash(Ctx ctx, int def, out ApricornColor color, out int count)
        {
            color = ApricornColor.Red;
            count = 0;
            if (def < 0 || def >= ctx.Items.Items.Count || ctx.Items.Items[def].Kind != ItemKind.HiddenSpot) return false;
            string key = ctx.Items.Items[def].Key;
            for (int c = 0; c < SeatState.ApricornColors; c++)
            {
                if (key != "hidden_spot." + ((ApricornColor)c).ToString().ToLowerInvariant() + "_apricorn_stash") continue;
                color = (ApricornColor)c;
                count = 4 - MapActions.ApricornGrade(color);
                return true;
            }
            return false;
        }

        /// <summary>앞면 우두머리 토큰(공개만 되고 싸우지 않은 것)이 그 칸에 있으면 그 토큰 색인, 없으면 −1.</summary>
        public static int FaceUpAlphaAt(GameState s, Ctx ctx, int cell)
        {
            if (cell < 0) return -1;
            for (int i = 0; i < s.Tokens.Count; i++)
            {
                var t = s.Tokens[i];
                if (t.Kind == TokenKind.HiddenSpot && t.FaceUp && t.Cell == cell && !t.Destroyed && t.Def >= 0 && ctx.Items.Items[t.Def].Kind == ItemKind.AlphaPokemon) return i;
            }
            return -1;
        }

        /// <summary>공개한 토큰의 결과를 처리한다. 배틀을 시작했으면 true(배틀이 턴을 끝낸다 — 맵 행동을 다시 열지 않는다).</summary>
        public static bool Resolve(GameState s, Ctx ctx, Emitter fx, int seat, int token)
        {
            var t = s.Tokens[token];
            var item = ctx.Items.Items[t.Def];
            var src = new SourceRef(SourceKind.HiddenSpot, t.Def);
            if (Stash(ctx, t.Def, out var color, out int count))
            {
                var st = s.Seats[seat];
                Told(fx, seat, t, SpotResult.Stash, count);
                int before = st.Apricorns[(int)color];
                st.Apricorns[(int)color] += count;
                if (fx.On)
                    fx.Add(new ApricornsGained
                    {
                        Seat = seat, Source = src,
                        Changes = new[] { new ResChange { Seat = seat, Res = ResKind.Apricorn, Key = (int)color, Before = before, After = before + count } },
                    });
                Remove(fx, seat, t);
                TrainerRules.Stashed(s, ctx, fx, seat, color); // veteran B: 같은 색 +2 · 이번 턴 이동 +1(rulings A37)
                return false;
            }
            switch (item.Kind)
            {
                case ItemKind.HiddenSpot:
                    if (item.Key == FallenStar)
                    {
                        Told(fx, seat, t, SpotResult.Star, s.FallenStars + 1);
                        FateRules.Deal(s, ctx, fx, src, seat, StarFate);
                        s.FallenStars++;
                        Remove(fx, seat, t);
                    }
                    else Told(fx, seat, t, SpotResult.Camp, 0);
                    return false; // 야영지는 앞면으로 남는다
                case ItemKind.Artifact:
                case ItemKind.CelestialArtifact:
                    Told(fx, seat, t, SpotResult.Relic, 0);
                    s.Seats[seat].Bag.Add(t.Def);
                    t.Loose = false;
                    Remove(fx, seat, t);
                    return false;
                case ItemKind.AlphaPokemon:
                    if (CraftRules.AlphaWarded(s, ctx, seat) || !StartAlpha(s, ctx, fx, seat, token))
                    {
                        if (t.Cell >= 0) Told(fx, seat, t, SpotResult.AlphaWarded, 0);   // 앞면으로 남았다(스튜 · 쓸 포켓몬 없음)
                        return false;
                    }
                    return true;
                default:
                    throw new InvalidOperationException($"숨겨진 장소 토큰의 아이템 {item.Key}({item.Kind})");
            }
        }

        /// <summary>
        /// 우두머리 배틀을 연다(A17 「Stand on the token and use the Pokédex to "Search"」): 그 칸의 지형·기후가 맞는 강한 카드(R1)를 뽑아 토큰을 붙인다.
        /// 맞는 카드가 없으면 강한 덱의 아무 카드 [가정 plan §5 #85 ②], 강한 덱이 비었으면 배틀 없이 토큰을 버린다 · 쓸 포켓몬이 없으면 앞면으로 남긴다 [가정 같은 번호]. 배틀을 시작했으면 true.
        /// </summary>
        public static bool StartAlpha(GameState s, Ctx ctx, Emitter fx, int seat, int token)
        {
            var t = s.Tokens[token];
            int cell = t.Cell;
            var c = ctx.Board[cell];
            if (!BattleRules.HasUsable(s, ctx, seat)) return false; // 앞면으로 남는다
            int card = MapActions.DrawMatching(s, ctx, DeckId.Strong, c.Biome, c.Climate, false);
            if (card < 0) card = MapActions.DrawMatching(s, ctx, DeckId.Strong, Biome.None, Climate.None, true);
            if (card >= 0) Told(fx, seat, t, SpotResult.Alpha, 0);
            Remove(fx, seat, t);
            if (card < 0) return false;
            BattleFlow.StartAlpha(s, ctx, fx, seat, card, cell);
            return true;
        }

        // 결과를 알린다(04 §8-8 26~30행 — 값이 바뀌는 이벤트보다 먼저)
        static void Told(Emitter fx, int seat, TokenState t, SpotResult result, int count)
        {
            if (fx.On) fx.Add(new SpotResolved { Seat = seat, Source = new SourceRef(SourceKind.HiddenSpot, t.Def), Cell = t.Cell, SpotKey = t.Def, Result = result, Count = count });
        }

        static void Remove(Emitter fx, int seat, TokenState t)
        {
            int cell = t.Cell;
            t.Cell = -1;
            if (fx.On) fx.Add(new TokenRemoved { Seat = seat, Cell = cell, TokenKind = TokenKind.HiddenSpot });
        }
    }
}
