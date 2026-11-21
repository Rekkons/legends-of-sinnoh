namespace Sinnoh.Rules
{
    /// <summary>
    /// 전투 피로(S7_7, A10, R12). 값 0~5는 배틀 중 포켓몬의 임시값이다(01 §2-2 PokemonState).
    /// 경계는 rulings A10 「피로 경계」: 1~5번째 굴림은 피로만 쌓고, 6번째 굴림부터 굴릴 때마다 3 피해.
    /// </summary>
    public static class FatigueRules
    {
        /// <summary>이 값이면 피로 상태(5번 연속 굴린 뒤). Lua 피로 주사위 값 6(FATIGUE_STATE_LEVEL) = 피로 5.</summary>
        public const int Max = 5;

        /// <summary>피로 상태에서 굴릴 때마다 그 포켓몬이 받는 피해.</summary>
        public const int Damage = 3;

        /// <summary>
        /// 다이스 롤러를 한 번 쓴다. 돌려주는 값 = 굴린 뒤 그 포켓몬이 받는 피로 피해(0 또는 3).
        /// 2번 공격하는 기술은 굴릴 때마다 부른다(A10, 원작 커뮤니티 확인). 포획 굴림은 CaptureAddsFatigue가 꺼져 있으면 세지도 피해를 주지도 않고, 연속도 끊지 않는다(A10 [가정]: 없던 굴림처럼 다룬다).
        /// </summary>
        public static int Roll(ref int fatigue, bool captureRoll, RuleOptions options)
        {
            if (captureRoll && !options.CaptureAddsFatigue) return 0;
            if (fatigue >= Max) return Damage;
            fatigue++;
            return 0;
        }

        /// <summary>교체되어 나가면(액티브 존이 비면) 0(S7_7, Lua update_panel).</summary>
        public static void SwitchOut(ref int fatigue) => fatigue = 0;
    }
}
