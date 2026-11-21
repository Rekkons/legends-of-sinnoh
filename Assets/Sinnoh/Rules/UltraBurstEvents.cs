namespace Sinnoh.Rules
{
    // 울트라 버스트 이벤트(01 §3-7 표의 M 6 행 — M6 「UB 셋업·맵」·「UB 턴」). 연출 행(04 §8-8 15·82~86)은 화면 묶음 몫이라 지금은 대기열 단계가 없다(PlayTiming.RowOf 0).

    /// <summary>왜곡 게이트 이동(p.26, 04 15행): 같은 이름의 게이트끼리, 이동력을 쓰지 않는다. FromGate·ToGate = 고정물 색인.</summary>
    public sealed class GateTravel : GameEvent
    {
        public int FromGate = U;
        public int ToGate = U;
        public int FromCell = U;
        public int ToCell = U;
    }

    /// <summary>UB 턴 시작(라운드 끝 — 모든 좌석이 한 번씩 턴을 마친 뒤, 04 82행). TotalMoves = 이번 턴 총 이동(R4), Breach = 턴 시작 균열 강도(%).</summary>
    public sealed class UbTurnStarted : GameEvent
    {
        public int Round = U;
        public int TotalMoves = U;
        public int Breach = U;
    }

    /// <summary>UB 한 칸 이동(R4, 04 82행). UbId = 그 UB(포켓몬 표 색인), StepIdx = 이번 UB 턴의 몇 번째 이동(0부터).</summary>
    public sealed class UbMoved : GameEvent
    {
        public int UbId = U;
        public int From = U;
        public int To = U;
    }

    /// <summary>UB가 밟은 칸의 숨겨진 장소·유물 토큰이 깨어진 세계의 빈 칸으로 갔다(R5, 04 83행). To = −1이면 버림. 앞·뒷면은 그대로.</summary>
    public sealed class TokenMoved : GameEvent
    {
        public TokenKind TokenKind = (TokenKind)U;
        public int From = U;
        public int To = U;
    }

    /// <summary>나무·산호·사당이 파괴됐다(R5·R6, 04 83·84행). ByUbId = 밟은 UB(포켓몬 표 색인), 네크로즈마의 원격 파괴는 −1.</summary>
    public sealed class CellDestroyed : GameEvent
    {
        public int Cell = U;
        public TokenKind TokenKind = (TokenKind)U;
        public int ByUbId = U;
    }

    /// <summary>네크로즈마의 원격 파괴(R6, 04 84행): 고른 칸들(뒤따르는 CellDestroyed와 같은 순서)과 정해진 수 ⌈인원/2⌉(남은 것이 모자라면 Cells가 더 짧다).</summary>
    public sealed class NecrozmaStrike : GameEvent
    {
        public int[] Cells;
        public int Count = U;
    }

    /// <summary>균열 강도(%)가 바뀌었다(R7, 04 83·84행).</summary>
    public sealed class BreachChanged : GameEvent
    {
        public int Before = U;
        public int After = U;
    }

    /// <summary>UB 턴 동안 균열이 10% 구간을 넘었다(R8, 04 85행). Before = 턴 시작 값, Brackets = 넘은 구간 수. 70% 이상(패배)이면 내지 않는다.</summary>
    public sealed class BreachBracket : GameEvent
    {
        public int Before = U;
        public int After = U;
        public int Brackets = U;
    }

    public sealed class BreachDeal
    {
        public int Seat = GameEvent.U;
        public int[] CardKeys;
    }

    /// <summary>좌석마다 받은 왜곡 균열 카드(B4, 04 85행 — 공개). 덱이 바닥나 못 받은 좌석은 빈 배열.</summary>
    public sealed class BreachCardsDealt : GameEvent
    {
        public BreachDeal[] PerSeat;
    }

    /// <summary>프리즘 아머가 이번에 적용한 피해 묶음에서 줄인 양(S17_4, 04 88행). Side = 지켜진 쪽(UB·네크로즈마), 뒤따르지 않고 그 HpChanged 묶음 바로 뒤에 온다.</summary>
    public sealed class PrismReduced : GameEvent
    {
        public int Side = U;
        public int PokemonId = U;
        public int Amount = U;
    }

    /// <summary>UB 격파로 프리즘 아머 1개가 옮겨 갔다(p.24, 04 88행): [0] 울트라 버스트 쪽(네크로즈마) · [1] 울트라 비스트 쪽.</summary>
    public sealed class PrismMoved : GameEvent
    {
        public int[] Before;
        public int[] After;
    }

    /// <summary>손의 균열 카드를 포켓몬의 기술 칸에 붙였다(S17_5, 04 87행 — 공개). ReplacedKey = 그 칸에서 버린 가르친 기술(없으면 −1).</summary>
    public sealed class BreachCardAttached : GameEvent
    {
        public int PokemonId = U;
        public int Slot = U;
        public int CardKey = U;
        public int ReplacedKey = U;
    }

    /// <summary>UB 격파 뒤 균열 카드 하나를 버렸다(p.24 — 왜곡 균열 버림 더미로). 손에 든 카드·보관한 카드면 PokemonId·Slot = −1(사용자 결정 84 U10·U11).</summary>
    public sealed class BreachCardDiscarded : GameEvent
    {
        public int PokemonId = U;
        public int Slot = U;
        public int CardKey = U;
    }

    /// <summary>UB 격파 뒤 붙은 균열 카드 하나를 자기 파티의 다른 포켓몬으로 옮겼다(p.24). ReplacedKey = 받는 칸에서 버린 가르친 기술(없으면 −1).</summary>
    public sealed class BreachCardMoved : GameEvent
    {
        public int CardKey = U;
        public int FromId = U;
        public int FromSlot = U;
        public int ToId = U;
        public int ToSlot = U;
        public int ReplacedKey = U;
    }

    /// <summary>2페이즈 네크로즈마의 썬/문 동전(p.25, 04 90행): Result 0 = 썬(황혼의 갈기 800:2) · 1 = 문(새벽의 날개 800:3). 2페이즈에 처음 도전할 때 한 번.</summary>
    public sealed class CoinFlip : GameEvent
    {
        public int Result = U;
    }

    /// <summary>네크로즈마의 페이즈가 바뀌었다(p.25 「proceeds to its next phase permanently」, 04 90행). Phase = 쓰러뜨린 페이즈, Next = 새 페이즈(3페이즈를 쓰러뜨리면 내지 않고 GameEnded).</summary>
    public sealed class NecrozmaPhase : GameEvent
    {
        public int Phase = U;
        public int Next = U;
    }

    /// <summary>네크로즈마에게 져서 탈락했다(p.25, 04 91행). Seat = 그 좌석.</summary>
    public sealed class Eliminated : GameEvent
    {
    }

    /// <summary>
    /// 아르세우스의 축복 1개를 썼다(p.23, 04 91행). Seat = 쓴 좌석, Target = −1이면 다음 UB 턴 건너뛰기 · 좌석 번호면 그 좌석 부활(뒤에 Revived). Left = 쓰고 남은 축복.
    /// </summary>
    public sealed class BlessingUsed : GameEvent
    {
        public int Target = U;
        public int Left = U;
    }

    /// <summary>탈락한 좌석이 부활했다(p.23 — 고향 마을에 선다, 04 91행). Seat = 그 좌석, Cell = 고향 마을 칸.</summary>
    public sealed class Revived : GameEvent
    {
        public int Cell = U;
    }

    /// <summary>아르세우스의 축복으로 이번 라운드 끝의 UB 턴을 건너뛰었다(04 91행 — 이동·네크로즈마의 파괴·균열 카드 딜이 없다).</summary>
    public sealed class UbTurnSkipped : GameEvent
    {
        public int Round = U;
    }

    /// <summary>아르세우스의 축복을 얻었다(04 93행): 실버디 격파·포획(Source = Rule:Silvally) · 아르세우스 운명 카드 3장(Source = Fate). Seat = 얻게 한 좌석, Total = 얻은 뒤 축복.</summary>
    public sealed class BlessingGained : GameEvent
    {
        public int Total = U;
    }

    /// <summary>
    /// 호수 시련이 나아갔다(S17_7, 04 92행). Lake = 호수(Shard 순서: 감정·지식·의지), Count / Goal = 지금까지 채운 것 / 넘기는 데 필요한 것
    /// (감정 = 피로 피해 1 / 1 · 지식 = 효과가 굉장했던 서로 다른 타입 수 / 2 · 의지 = 내 최근 두 턴의 크리티컬 합 / 2), Done = 이것으로 넘겼다(면역은 이 배틀 턴까지).
    /// </summary>
    public sealed class TrialProgress : GameEvent
    {
        public int Lake = U;
        public int Count = U;
        public int Goal = U;
        public bool Done;
    }

    /// <summary>
    /// 울트라 온슬롯 카드를 뽑아 바로 적용했다(rulings A21-1 · A56, 04 89행). Seat = 그 쪽(SeatId.UltraBeast), CardKey = 카드(카드 표 색인 — Source.Id와 같다), Effect = 카드 번호 1~6(UbOnslaught).
    /// 효과의 결과(회복 · 피로 · 면 바꾸기 · 피해)는 뒤따르는 이벤트가 같은 출처로 싣는다.
    /// </summary>
    public sealed class OnslaughtDrawn : GameEvent
    {
        public int CardKey = U;
        public int Effect = U;
    }

    /// <summary>호수 수호신을 쓰러뜨리거나 잡았다(p.26, 04 92행): 이제부터 모든 좌석이 턴마다 이동력 +2. Seat = 이긴 좌석, Lake = 호수, Total = 그런 수호신 수.</summary>
    public sealed class GuardianDown : GameEvent
    {
        public int Lake = U;
        public int Total = U;
    }
}
