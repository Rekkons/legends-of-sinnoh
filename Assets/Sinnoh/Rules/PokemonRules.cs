using System;
using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>포켓몬 인스턴스·파티 질의와 조작(01 §2-2 Pokemon·SeatState.Party). 파티는 앞에서부터 빈칸 없이 채운다.</summary>
    public static class PokemonRules
    {
        /// <summary>우두머리 토큰 체력 보너스(A17 「The token increases the health of the user by 4」).</summary>
        public const int AlphaHealthBonus = 4;

        /// <summary>최대 체력 = 카드 체력 + 우두머리 +4 + 트레이너 카드 보정(PokemonState.HealthMod — TrainerRules.Refresh가 적어 둔다, rulings A32) + 균열 카드(BreachRules.HealthBonus). 1 아래로 내려가지 않는다.</summary>
        public static int MaxHp(Ctx ctx, PokemonState p) => Math.Max(1, ctx.Cards.Pokemon[p.Def].Health + (p.Alpha ? AlphaHealthBonus : 0) + p.HealthMod + BreachRules.HealthBonus(ctx, p));

        public static int Hp(Ctx ctx, PokemonState p) => Math.Max(0, MaxHp(ctx, p) - p.Damage);

        public static bool IsFainted(Ctx ctx, PokemonState p) => p.Damage >= MaxHp(ctx, p);

        /// <summary>새 인스턴스(빈 자리 Def = −1을 먼저 다시 쓴다). 기술 칸은 비어 있다.</summary>
        public static int Spawn(GameState s, int def, int owner, int deck)
        {
            if (def < 0) throw new ArgumentOutOfRangeException(nameof(def), def, "포켓몬 표 색인");
            int id = -1;
            for (int i = 0; i < s.Pokemon.Count; i++)
                if (s.Pokemon[i].Def < 0) { id = i; break; }
            if (id < 0)
            {
                id = s.Pokemon.Count;
                s.Pokemon.Add(new PokemonState());
            }
            s.Pokemon[id].CopyFrom(new PokemonState { Def = def, Owner = owner, Deck = deck });
            return id;
        }

        /// <summary>인스턴스를 비운다(자리는 다음 Spawn이 다시 쓴다).</summary>
        public static void Free(GameState s, int id) => s.Pokemon[id].CopyFrom(new PokemonState());

        public static int PartyCount(SeatState seat)
        {
            int n = 0;
            foreach (int p in seat.Party)
                if (p >= 0) n++;
            return n;
        }

        public static int PartyIndexOf(SeatState seat, int pokemon)
        {
            if (pokemon < 0) return -1;
            for (int i = 0; i < seat.Party.Length; i++)
                if (seat.Party[i] == pokemon) return i;
            return -1;
        }

        /// <summary>파티 맨 뒤 빈칸에 넣는다. 칸 번호를 돌려준다(열린 칸이 없으면 −1, 넣지 않음).</summary>
        public static int AddToParty(SeatState seat, int pokemon)
        {
            int n = PartyCount(seat);
            if ((!seat.Ranch && n >= seat.OpenSlots) || n >= seat.Party.Length) return -1; // pokemon_ranger B: 잠긴 칸에도 둔다(rulings A39)
            seat.Party[n] = pokemon;
            return n;
        }

        /// <summary>그 포켓몬이 그 아이템을 지녔나(지닌물건 두 칸 — backpacker R, rulings A39).</summary>
        public static bool HoldsItem(PokemonState p, int item) => item >= 0 && p != null && (p.HeldItem == item || p.HeldItem2 == item);

        /// <summary>그 아이템을 지닌 칸을 비운다(둘째 칸이면 둘째 칸).</summary>
        public static void DropItem(PokemonState p, int item)
        {
            if (p.HeldItem == item) p.HeldItem = -1;
            else if (p.HeldItem2 == item) p.HeldItem2 = -1;
        }

        /// <summary>파티 칸 index의 포켓몬이 배틀에 나올 수 있나 — 열린 칸만(pokemon_ranger B가 잠긴 칸에 둔 포켓몬은 못 나온다, rulings A39).</summary>
        public static bool Fights(SeatState seat, int index) => index < seat.OpenSlots;

        /// <summary>파티에서 빼고 뒤를 당긴다.</summary>
        public static void RemoveFromParty(SeatState seat, int pokemon)
        {
            int i = PartyIndexOf(seat, pokemon);
            if (i < 0) return;
            for (int k = i; k < seat.Party.Length - 1; k++) seat.Party[k] = seat.Party[k + 1];
            seat.Party[seat.Party.Length - 1] = -1;
        }

        /// <summary>
        /// 쓸 수 있는 기술 후보(시그니처 → 배운 기술 칸 순서, 같은 기술은 한 번 → 갤럭시단 포켓몬이면 플랜 B, rulings G13). BattleAction Legal 순서다.
        /// </summary>
        public static void MoveOptions(Ctx ctx, PokemonState p, List<int> into)
        {
            into.Clear();
            into.Add(ctx.Cards.Pokemon[p.Def].Signature);
            foreach (int m in p.Moves)
                if (m >= 0 && !into.Contains(m)) into.Add(m);
            if (p.Owner == SeatId.Galactic) into.Add(BattleRules.PlanB);
            if (LegendRules.HasAlphaStrike(p)) into.Add(BattleRules.AlphaStrike); // 배운 기술이 없는 전설(rulings A26) — 타입은 답마다(ChoiceRules)
        }

        /// <summary>기술의 공격력(주사위 수). 「?」(데이터 null)는 0 — 핸들러는 M5. 플랜 B는 3, 알파 스트라이크는 4.</summary>
        public static int AttackStrength(Ctx ctx, int move) => BattleRules.IsTactic(move) ? BattleRules.MoveStrength(ctx, move) : ctx.Cards.Moves[move].AttackStrength ?? 0;
    }
}
