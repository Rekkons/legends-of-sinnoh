using static Sinnoh.Rules.PokemonType;

namespace Sinnoh.Rules
{
    /// <summary>
    /// 타입(00 §5 용어 35). 이름·순서는 Lua <c>config.type_chart</c> 키 순서(raw/lua/_global.lua:12-36).
    /// <c>Type</c>은 System.Type과 겹쳐서 PokemonType이다. 타입없음(Typeless) 공격은 상성을 무시한다(S7_4).
    /// </summary>
    public enum PokemonType
    {
        Typeless,
        Normal,
        Fire,
        Water,
        Electric,
        Grass,
        Ice,
        Fighting,
        Poison,
        Ground,
        Flying,
        Psychic,
        Bug,
        Rock,
        Ghost,
        Dragon,
        Dark,
        Steel,
        Fairy,
    }

    /// <summary>상성 보정(용어 36). 값 = 피해 증감(S7_4, Lua WEAK 2 · RESIST −2 · IMMUNE −4). Neutral = 표에 없는 칸.</summary>
    public enum Effectiveness
    {
        NotEffective = -4,
        NotVeryEffective = -2,
        Neutral = 0,
        SuperEffective = 2,
    }

    /// <summary>
    /// 18타입 + Typeless 상성표(01 §6-5). raw/lua/_global.lua:7-37을 그대로 옮긴 코드 상수다(콘텐츠가 아니라 규칙).
    /// 행 = 공격 기술 타입, 칸 = 방어 포켓몬 타입. 적히지 않은 칸은 Neutral. Typeless는 행·열 모두 비어 있다.
    /// </summary>
    public static class TypeChart
    {
        public const int TypeCount = 19;

        static readonly Effectiveness[] Table = Build();

        public static Effectiveness Of(PokemonType attack, PokemonType defender) =>
            Table[(int)attack * TypeCount + (int)defender];

        /// <summary>
        /// ③ 상성 보정 합. 방어 타입마다 더한다(복합 타입 합산, Lua get_effectiveness). 단일 타입이면 secondDefender = Typeless(0).
        /// </summary>
        public static int Bonus(PokemonType attack, PokemonType defender, PokemonType secondDefender = Typeless) =>
            (int)Of(attack, defender) + (int)Of(attack, secondDefender);

        static Effectiveness[] Build()
        {
            const Effectiveness W = Effectiveness.SuperEffective, R = Effectiveness.NotVeryEffective, I = Effectiveness.NotEffective;
            var t = new Effectiveness[TypeCount * TypeCount];

            void Row(PokemonType attack, params (PokemonType defender, Effectiveness e)[] cells)
            {
                foreach (var c in cells) t[(int)attack * TypeCount + (int)c.defender] = c.e;
            }

            // _global.lua:13-36 순서·내용 그대로(Typeless = {})
            Row(Normal, (Rock, R), (Ghost, I), (Steel, R));
            Row(Fire, (Fire, R), (Water, R), (Grass, W), (Ice, W), (Bug, W), (Rock, R), (Dragon, R), (Steel, W));
            Row(Water, (Fire, W), (Water, R), (Grass, R), (Ground, W), (Rock, W), (Dragon, R));
            Row(Electric, (Water, W), (Electric, R), (Grass, R), (Ground, I), (Flying, W), (Dragon, R));
            Row(Grass, (Fire, R), (Water, W), (Grass, R), (Poison, R), (Ground, W), (Flying, R), (Bug, R),
                (Rock, W), (Dragon, R), (Steel, R));
            Row(Ice, (Fire, R), (Water, R), (Grass, W), (Ice, R), (Ground, W), (Flying, W), (Dragon, W),
                (Steel, R));
            Row(Fighting, (Normal, W), (Ice, W), (Poison, R), (Flying, R), (Psychic, R), (Bug, R), (Rock, W),
                (Ghost, I), (Dark, W), (Steel, W), (Fairy, R));
            Row(Poison, (Grass, W), (Poison, R), (Ground, R), (Rock, R), (Ghost, R), (Steel, I), (Fairy, W));
            Row(Ground, (Fire, W), (Electric, W), (Grass, R), (Poison, W), (Flying, I), (Bug, R), (Rock, W),
                (Steel, W));
            Row(Flying, (Electric, R), (Grass, W), (Fighting, W), (Bug, W), (Rock, R), (Steel, R));
            Row(Psychic, (Fighting, W), (Poison, W), (Psychic, R), (Dark, I), (Steel, R));
            Row(Bug, (Fire, R), (Grass, W), (Fighting, R), (Poison, R), (Flying, R), (Psychic, W), (Ghost, R),
                (Dark, W), (Steel, R), (Fairy, R));
            Row(Rock, (Fire, W), (Ice, W), (Fighting, R), (Ground, R), (Flying, W), (Bug, W), (Steel, R));
            Row(Ghost, (Normal, I), (Psychic, W), (Ghost, W), (Dark, R));
            Row(Dragon, (Dragon, W), (Steel, R), (Fairy, I));
            Row(Dark, (Fighting, R), (Psychic, W), (Ghost, W), (Dark, R), (Fairy, R));
            Row(Steel, (Fire, R), (Water, R), (Electric, R), (Ice, W), (Rock, W), (Steel, R), (Fairy, W));
            Row(Fairy, (Fire, R), (Fighting, W), (Poison, R), (Dragon, W), (Dark, W), (Steel, R));
            return t;
        }
    }
}
