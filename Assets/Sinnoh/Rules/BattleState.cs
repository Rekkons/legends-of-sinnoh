using System.Collections.Generic;

namespace Sinnoh.Rules
{
    /// <summary>배틀 종류(01 §6-1).</summary>
    public enum BattleKind { Wild, Swarm, Trainer, Galactic, Legendary, Alpha, God, UltraBeast, Necrozma, Trial, Traitor }

    /// <summary>
    /// 배틀 단계(01 §6-1). 처리 중 위치를 데이터로 둬 어느 대기 지점에서든 복제·저장한다(01 §2-3).
    /// DamageWindow(M3-f) = 피해 계산과 기술 효과(PreDamage) 뒤, 피해 적용 전에 BeforeDamage 반응 창을 여는 자리 — 효과 프레임이 다 돈 뒤에 연다.
    /// Capture(결정 76 ③) = 볼을 굴린 뒤 굴린 뒤 창(디아루가)이 닫히면 포획 판정을 잇는 자리(굴린 면 = Faces, 볼 = CaptureBall). 저장된 값이 밀리지 않게 맨 끝에 둔다.
    /// </summary>
    public enum BattleStep { SendIn, Order, TurnStart, SelectAction, PreRoll, Roll, PostRoll, PreDamage, DamageWindow, ApplyDamage, TurnEnd, FaintCheck, Replace, End, Capture, Concluding }

    /// <summary>
    /// 배틀 한쪽(01 §6-1 Sides). 직전 기술과 피로는 포켓몬마다라 PokemonState(LastMove·Fatigue)에 둔다.
    /// </summary>
    public sealed class BattleSide : IStatePart<BattleSide>
    {
        /// <summary>주인: 좌석 0~5 / SeatId.Wild·Galactic·UltraBeast.</summary>
        public int Owner = SeatId.System;
        /// <summary>이 쪽 선택에 답하는 좌석(가상 응답자 포함, 01 §3-3).</summary>
        public int Responder = SeatId.System;
        /// <summary>명단(포켓몬 인스턴스 색인, 출전 순서).</summary>
        public List<int> Roster = new List<int>();
        /// <summary>출전 중 인스턴스. 없으면 −1.</summary>
        public int Active = -1;
        /// <summary>우선권 토큰을 가졌다(S7_1).</summary>
        public bool InitiativeToken;
        /// <summary>은하의 응징 저장량(A13).</summary>
        public int Retribution;
        /// <summary>비스트 부스트 다음 보너스.</summary>
        public int BeastBoost;
        /// <summary>「첫 공격」을 했다(폭발수정·별 방패).</summary>
        public bool FirstAttackDone;
        /// <summary>출전 중 포켓몬을 마지막으로 깎은 출처(기절 알림의 ParentSource, 01 §3-6 ⑦). 출전할 때 None.</summary>
        public SourceRef LastHit = SourceRef.None;
        /// <summary>마지막으로 깎은 포켓몬(Fainted.ById, 갤럭시단 교체 금지 A16). 없으면 −1.</summary>
        public int LastHitBy = -1;
        /// <summary>이번 배틀에서 이 쪽 포켓몬이 기절했다(기권 S7_8은 그 뒤부터, rulings G13). 배틀 전에 기절해 있던 포켓몬은 세지 않는다.</summary>
        public bool FaintedThisBattle;
        /// <summary>이 쪽에 다음에 나오는 포켓몬이 받을 넘친 피해(renegade_fury·overcut 「any remaining damage from this attack is applied to the next Pokémon switched in」, rulings G20). 0 = 없음.</summary>
        public int Carry;
        /// <summary>넘친 피해의 출처(그 기술).</summary>
        public SourceRef CarrySource = SourceRef.None;
        /// <summary>이어서 실패한 포획 수(여행 스탬프 1 「2 turns in a row」 — 기술을 쓰면 0, StampRules).</summary>
        public int FailedCaptures;
        /// <summary>이 배틀에서 이 쪽 포켓몬이 쓰러진 수(roller_skater B 「only flee after defeating two of your Pokémon」, rulings A36).</summary>
        public int Fallen;
        /// <summary>chef R 「Masterwork Stew」를 이 배틀에서 썼다(배틀 끝까지 — 그 쪽 포켓몬의 자기 타입 기술 공격력 +1, rulings A34).</summary>
        public bool Masterwork;
        /// <summary>lady R 「Once per battle, heal all damage and status effects from a non-fainted Pokémon in your party」를 이 배틀에서 썼다(rulings A35).</summary>
        public bool Potion;
        /// <summary>guitarist R로 앰프를 두 배로 썼다 — 이 배틀에서 이 쪽은 앰프를 다시 못 쓴다(rulings A41).</summary>
        public bool AmpSpent;
        /// <summary>출전 중 포켓몬의 「나올 때」 효과(균열 카드 status_overload)가 아직 남았다 — 상대가 나와 있어야 한다(BreachRules.SentIn).</summary>
        public bool Entered;

        public void CopyFrom(BattleSide o)
        {
            Owner = o.Owner;
            Responder = o.Responder;
            StateParts.CopyInts(Roster, o.Roster);
            Active = o.Active;
            InitiativeToken = o.InitiativeToken;
            Retribution = o.Retribution;
            BeastBoost = o.BeastBoost;
            FirstAttackDone = o.FirstAttackDone;
            LastHit = o.LastHit;
            LastHitBy = o.LastHitBy;
            FaintedThisBattle = o.FaintedThisBattle;
            Carry = o.Carry;
            CarrySource = o.CarrySource;
            FailedCaptures = o.FailedCaptures;
            Fallen = o.Fallen;
            Masterwork = o.Masterwork;
            Potion = o.Potion;
            AmpSpent = o.AmpSpent;
            Entered = o.Entered;
        }

        public void Walk(IStateWriter w)
        {
            w.Int(Owner);
            w.Int(Responder);
            StateParts.Ints(w, Roster);
            w.Int(Active);
            StateParts.Bool(w, InitiativeToken);
            w.Int(Retribution);
            w.Int(BeastBoost);
            StateParts.Bool(w, FirstAttackDone);
            LastHit.Walk(w);
            w.Int(LastHitBy);
            StateParts.Bool(w, FaintedThisBattle);
            w.Int(Carry);
            CarrySource.Walk(w);
            w.Int(FailedCaptures);
            w.Int(Fallen);
            StateParts.Bool(w, Masterwork);
            StateParts.Bool(w, Potion);
            StateParts.Bool(w, AmpSpent);
            StateParts.Bool(w, Entered);
        }

        public void Read(IStateReader r)
        {
            Owner = r.Int();
            Responder = r.Int();
            StateParts.ReadInts(r, Roster);
            Active = r.Int();
            InitiativeToken = StateParts.ReadBool(r);
            Retribution = r.Int();
            BeastBoost = r.Int();
            FirstAttackDone = StateParts.ReadBool(r);
            LastHit = SourceRef.Read(r);
            LastHitBy = r.Int();
            FaintedThisBattle = StateParts.ReadBool(r);
            Carry = r.Int();
            CarrySource = SourceRef.Read(r);
            FailedCaptures = r.Int();
            Fallen = r.Int();
            Masterwork = StateParts.ReadBool(r);
            Potion = StateParts.ReadBool(r);
            AmpSpent = StateParts.ReadBool(r);
            Entered = StateParts.ReadBool(r);
        }
    }

    /// <summary>
    /// 진행 중인 배틀(01 §6-1). 없으면 GameState.Battle = null. 체력·상태이상은 PokemonState에 바로 쓴다(배틀 뒤에도 남는다, S7_8).
    /// </summary>
    public sealed class BattleState : IStatePart<BattleState>
    {
        public const int SideCount = 2;

        /// <summary>BattleId(GameState.NextBattleId). 이벤트를 배틀 하나로 묶는다(01 §3-6 ①-2).</summary>
        public int Id;
        public BattleKind Kind;
        /// <summary>배틀 칸. 원격(도전 등)이면 −1.</summary>
        public int Cell = -1;
        /// <summary>조우가 나온 토큰(나무·산호면 A8 규토리). 없으면 −1.</summary>
        public int Token = -1;
        /// <summary>[0] 도전자, [1] 상대(01 §3-7 Side).</summary>
        public BattleSide[] Sides = { new BattleSide(), new BattleSide() };
        public BattleStep Step;
        /// <summary>배틀 턴 번호(1부터, 0 = 시작 전).</summary>
        public int Turn;
        /// <summary>지금 행동하는 쪽(0·1). 없으면 −1.</summary>
        public int ActiveSide = -1;
        /// <summary>포획 불가(UB·네크로즈마·uncapturable).</summary>
        public bool Uncapturable;

        // 진행 중인 공격(01 §6-1): 기술, 보정 뒤 공격력, 굴린 면(순서대로), 한 턴 피해 버퍼(§6-4)
        /// <summary>쓰는 기술 색인. 없으면 −1.</summary>
        public int Move = -1;
        public int AttackStrength;
        public List<AttackDieFace> Faces = new List<AttackDieFace>();
        /// <summary>
        /// 한 턴 피해·회복 버퍼(p.13 동시 적용): (포켓몬 인스턴스, 체력 증감, 출처 Kind, 출처 Id) 넷씩 이어 둔다. 모아서 동시에 적용한 뒤 기절을 본다.
        /// 공격 피해(Move), 옮긴 은하의 응징(Rule:GalacticRetribution, G5), 피로 피해(Rule:Fatigue, rulings G13)가 들어간다.
        /// </summary>
        public List<int> DamageBuffer = new List<int>();
        /// <summary>교체·기절 교체가 끝나면 순서를 다시 정한다(S7_5).</summary>
        public bool Reorder;
        /// <summary>이번 기절 교체에서 기절하지 않은 쪽에 교체 창을 이미 열었다(S7_5, rulings G13).</summary>
        public bool SwitchOffered;
        /// <summary>기라티나가 더한 이번 배틀 턴 기술의 공격력(rulings A24 ③ — 굴리기 전 창에서 내고 배틀 턴이 시작될 때 0, BattleRules.RollPower가 센다).</summary>
        public int FatePower;
        /// <summary>이번 공격의 타입을 효과가 정했다(hidden_power·judgement — 고른 PokemonType 값). −1 = 기술 카드의 타입(rulings G20).</summary>
        public int TypeTo = -1;
        /// <summary>이번 공격의 공격력을 효과가 정했다(belch 「consume 1 Apricorn to increase attack strength to 7」 — 「공격력 = n」, G14). 0 = 없음.</summary>
        public int StrengthTo;
        /// <summary>이번 배틀 턴 끝(턴 끝 효과 뒤)에 교체할 수 있는 포켓몬(u_turn 등 「The user may switch out」, rulings G20). −1 = 없음.</summary>
        public int SwitchAfter = -1;

        // ── M5 「기술 효과 — 핸들러 갈래」(rulings G21) ──
        /// <summary>이번 공격의 공격력·타입을 정하는 기술(mimic·metronome 「attack the target with this move」 — 부른 기술). −1 = Move(BattleRules.StrengthMove).</summary>
        public int Called = -1;
        /// <summary>이번 공격의 효과를 읽는 기술(kinesis 「Copy the effect of a move」 · mimic·metronome의 부른 기술 — 나중에 정한 것). −1 = Move(BattleRules.EffectMove).</summary>
        public int EffectsOf = -1;
        /// <summary>future_sight의 맞히기(0 = 크리티컬이 더 많다 · 1 = 더 적다 · 2 = 같다). −1 = 없음.</summary>
        public int Guess = -1;
        /// <summary>이번 배틀 턴에 둘째 기술을 쓸 수 있는 포켓몬(quick_attack 등 「The user may use a second move this turn」 — 처리기 second_move). −1 = 없음.</summary>
        public int SecondBy = -1;
        /// <summary>둘째 기술을 준 효과 절(이어 묻는 프레임·한도) · 그때의 대상(첫 공격의 받는 포켓몬).</summary>
        public int SecondEffect = -1, SecondTarget = -1;
        /// <summary>지금 공격이 둘째 기술이다(이어 걸지 못한다 — 원작 커뮤니티 확인).</summary>
        public bool InSecond;
        /// <summary>둘째 기술이 있었던 배틀 턴의 첫 기술(직전 기술 둘째 칸 — PokemonState.LastMove2). −1 = 없음.</summary>
        public int FirstMove = -1;
        /// <summary>고른 추가 공격의 기술(둘째 기술 · encore의 마지막 기술). −1 = 없음. ExtraFinal = 마지막 기술이다.</summary>
        public int ExtraMove = -1;
        public bool ExtraFinal;
        /// <summary>추가 공격으로 고른 알파 스트라이크의 타입(rulings A26 ① — 그 공격의 TypeTo). −1 = 없음.</summary>
        public int ExtraType = -1;
        /// <summary>encore의 마지막 기술이 진행 중이면 원래 차례 쪽(그 공격이 끝나면 되돌린다)과 원래 기술. −1 = 아니다.</summary>
        public int FinalSide = -1, FinalMove = -1;
        /// <summary>이번 배틀 턴에 받은 피해(포켓몬, 양 둘씩) · 바로 앞 배틀 턴에 받은 피해(destiny_bond·rampart_riposte 「damage taken last turn」).</summary>
        public List<int> TakenNow = new List<int>(), TakenLast = new List<int>();
        /// <summary>이 배틀에서 쓴 기술(포켓몬, 기술 둘씩 — rollout·lift_off 「Each time this attack is used this battle」).</summary>
        public List<int> UsedMoves = new List<int>();
        /// <summary>굴린 뒤 창(디아루가)을 기다리는 포획 굴림의 볼(Ball 값 — 결정 76 ③, Step = Capture 동안만). −1 = 없음.</summary>
        public int CaptureBall = -1;
        /// <summary>이번 공격에서 이미 지난 트레이너 카드 물음 자리(TrainerRules.Asked… 비트 — 굴리기 전 · 굴림 · 굴린 뒤, rulings A34).</summary>
        public int CardAsk;
        /// <summary>이번 공격에 쓴 앰프 피해(guitarist B·R — 효과 피해 ②, 공격마다 0으로, rulings A41).</summary>
        public int AmpDamage;
        /// <summary>hex_maniac: 지금 상태를 거는 op에 물어 두었다 · 상태 주사위로 바꾼 상태(없으면 −1) · 이미 걸린 상태가 있어도 더 건다(rulings A34).</summary>
        public bool HexAsked;
        public int HexAs = -1;
        public bool HexExtra;
        /// <summary>scuba_diver R(rulings A35): 이번 공격(물 타입 기술) 뒤 교체할 수 있는 포켓몬. 없으면 −1.</summary>
        public int ScubaBy = -1;
        /// <summary>team_rocket R 「you may travel to any tile on the map」(rulings A35): 배틀을 닫기 전에 갈 칸을 물었다 · 묻는 좌석 · 고른 칸(−1 = 고향 마을) · 미뤄 둔 끝 결과(BattleStep.Concluding).</summary>
        public bool BlastAsked;
        public int BlastSeat = -1, BlastTo = -1;
        public int EndResult, EndWinner = -1;
        /// <summary>이번 배틀 턴에 프리즘 아머가 이미 줄인 피해(S17_4 「per turn」 — 배틀 턴이 시작될 때 0, UbBattle.Reduce).</summary>
        public int PrismUsed;
        /// <summary>
        /// 호수 시련의 진행(BattleKind.Trial, UbTrial — rulings A55): TrialNow = 지식이면 효과가 굉장했던 공격 타입의 비트 · 의지면 내 이번 배틀 턴의 크리티컬 수, TrialPrev = 의지의 바로 앞 내 배틀 턴 크리티컬 수.
        /// TrialTurn = 이 배틀에서 시련을 넘긴 배틀 턴(그 턴까지 면역 — p.26 「until the turn after」). −1 = 이 배틀에서 넘기지 않았다.
        /// </summary>
        public int TrialNow, TrialPrev, TrialTurn = -1;
        /// <summary>
        /// 울트라 온슬롯(UbOnslaught — rulings A21-1 · A56): Onslaught = 이번 배틀 턴에 UB·네크로즈마가 뽑은 카드 번호(1~6, 배틀 턴이 시작될 때 0),
        /// OnslaughtThorns = 카드 4 「When next attacked」가 다음 공격을 기다린다.
        /// </summary>
        public int Onslaught;
        public bool OnslaughtThorns;

        public void CopyFrom(BattleState o)
        {
            Id = o.Id;
            Kind = o.Kind;
            Cell = o.Cell;
            Token = o.Token;
            if (Sides.Length != o.Sides.Length)
            {
                Sides = new BattleSide[o.Sides.Length];
                for (int i = 0; i < Sides.Length; i++) Sides[i] = new BattleSide();
            }
            for (int i = 0; i < Sides.Length; i++) Sides[i].CopyFrom(o.Sides[i]);
            Step = o.Step;
            Turn = o.Turn;
            ActiveSide = o.ActiveSide;
            Uncapturable = o.Uncapturable;
            Move = o.Move;
            AttackStrength = o.AttackStrength;
            Faces.Clear();
            Faces.AddRange(o.Faces);
            StateParts.CopyInts(DamageBuffer, o.DamageBuffer);
            Reorder = o.Reorder;
            SwitchOffered = o.SwitchOffered;
            FatePower = o.FatePower;
            TypeTo = o.TypeTo;
            StrengthTo = o.StrengthTo;
            SwitchAfter = o.SwitchAfter;
            Called = o.Called;
            EffectsOf = o.EffectsOf;
            Guess = o.Guess;
            SecondBy = o.SecondBy;
            SecondEffect = o.SecondEffect;
            SecondTarget = o.SecondTarget;
            InSecond = o.InSecond;
            FirstMove = o.FirstMove;
            ExtraMove = o.ExtraMove;
            ExtraFinal = o.ExtraFinal;
            ExtraType = o.ExtraType;
            FinalSide = o.FinalSide;
            FinalMove = o.FinalMove;
            StateParts.CopyInts(TakenNow, o.TakenNow);
            StateParts.CopyInts(TakenLast, o.TakenLast);
            StateParts.CopyInts(UsedMoves, o.UsedMoves);
            CaptureBall = o.CaptureBall;
            CardAsk = o.CardAsk;
            AmpDamage = o.AmpDamage;
            HexAsked = o.HexAsked;
            HexAs = o.HexAs;
            HexExtra = o.HexExtra;
            ScubaBy = o.ScubaBy;
            BlastAsked = o.BlastAsked;
            BlastSeat = o.BlastSeat;
            BlastTo = o.BlastTo;
            EndResult = o.EndResult;
            EndWinner = o.EndWinner;
            PrismUsed = o.PrismUsed;
            TrialNow = o.TrialNow;
            TrialPrev = o.TrialPrev;
            TrialTurn = o.TrialTurn;
            Onslaught = o.Onslaught;
            OnslaughtThorns = o.OnslaughtThorns;
        }

        public void Walk(IStateWriter w)
        {
            w.Int(Id);
            w.Int((int)Kind);
            w.Int(Cell);
            w.Int(Token);
            w.Int(Sides.Length);
            for (int i = 0; i < Sides.Length; i++) Sides[i].Walk(w);
            w.Int((int)Step);
            w.Int(Turn);
            w.Int(ActiveSide);
            StateParts.Bool(w, Uncapturable);
            w.Int(Move);
            w.Int(AttackStrength);
            w.Int(Faces.Count);
            for (int i = 0; i < Faces.Count; i++) w.Int((int)Faces[i]);
            StateParts.Ints(w, DamageBuffer);
            StateParts.Bool(w, Reorder);
            StateParts.Bool(w, SwitchOffered);
            w.Int(FatePower);
            w.Int(TypeTo);
            w.Int(StrengthTo);
            w.Int(SwitchAfter);
            w.Int(Called);
            w.Int(EffectsOf);
            w.Int(Guess);
            w.Int(SecondBy);
            w.Int(SecondEffect);
            w.Int(SecondTarget);
            StateParts.Bool(w, InSecond);
            w.Int(FirstMove);
            w.Int(ExtraMove);
            StateParts.Bool(w, ExtraFinal);
            w.Int(ExtraType);
            w.Int(FinalSide);
            w.Int(FinalMove);
            StateParts.Ints(w, TakenNow);
            StateParts.Ints(w, TakenLast);
            StateParts.Ints(w, UsedMoves);
            w.Int(CaptureBall);
            w.Int(CardAsk);
            w.Int(AmpDamage);
            StateParts.Bool(w, HexAsked);
            w.Int(HexAs);
            StateParts.Bool(w, HexExtra);
            w.Int(ScubaBy);
            StateParts.Bool(w, BlastAsked);
            w.Int(BlastSeat);
            w.Int(BlastTo);
            w.Int(EndResult);
            w.Int(EndWinner);
            w.Int(PrismUsed);
            w.Int(TrialNow);
            w.Int(TrialPrev);
            w.Int(TrialTurn);
            w.Int(Onslaught);
            StateParts.Bool(w, OnslaughtThorns);
        }

        public void Read(IStateReader r)
        {
            Id = r.Int();
            Kind = (BattleKind)r.Int();
            Cell = r.Int();
            Token = r.Int();
            int sides = r.Count();
            if (Sides.Length != sides)
            {
                Sides = new BattleSide[sides];
                for (int i = 0; i < Sides.Length; i++) Sides[i] = new BattleSide();
            }
            for (int i = 0; i < Sides.Length; i++) Sides[i].Read(r);
            Step = (BattleStep)r.Int();
            Turn = r.Int();
            ActiveSide = r.Int();
            Uncapturable = StateParts.ReadBool(r);
            Move = r.Int();
            AttackStrength = r.Int();
            int faces = r.Count();
            Faces.Clear();
            for (int i = 0; i < faces; i++) Faces.Add((AttackDieFace)r.Int());
            StateParts.ReadInts(r, DamageBuffer);
            Reorder = StateParts.ReadBool(r);
            SwitchOffered = StateParts.ReadBool(r);
            FatePower = r.Int();
            TypeTo = r.Int();
            StrengthTo = r.Int();
            SwitchAfter = r.Int();
            Called = r.Int();
            EffectsOf = r.Int();
            Guess = r.Int();
            SecondBy = r.Int();
            SecondEffect = r.Int();
            SecondTarget = r.Int();
            InSecond = StateParts.ReadBool(r);
            FirstMove = r.Int();
            ExtraMove = r.Int();
            ExtraFinal = StateParts.ReadBool(r);
            ExtraType = r.Int();
            FinalSide = r.Int();
            FinalMove = r.Int();
            StateParts.ReadInts(r, TakenNow);
            StateParts.ReadInts(r, TakenLast);
            StateParts.ReadInts(r, UsedMoves);
            CaptureBall = r.Int();
            CardAsk = r.Int();
            AmpDamage = r.Int();
            HexAsked = StateParts.ReadBool(r);
            HexAs = r.Int();
            HexExtra = StateParts.ReadBool(r);
            ScubaBy = r.Int();
            BlastAsked = StateParts.ReadBool(r);
            BlastSeat = r.Int();
            BlastTo = r.Int();
            EndResult = r.Int();
            EndWinner = r.Int();
            PrismUsed = r.Int();
            TrialNow = r.Int();
            TrialPrev = r.Int();
            TrialTurn = r.Int();
            Onslaught = r.Int();
            OnslaughtThorns = StateParts.ReadBool(r);
        }
    }
}
