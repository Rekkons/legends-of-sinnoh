using System;
using System.Collections.Generic;

namespace Sinnoh.Rules.Tutorial
{
    /// <summary>단계가 끝나는 법(07 §7 `advance`).</summary>
    public enum StepAdvance
    {
        /// <summary>말풍선 [다음]. 명령을 받지 않는다.</summary>
        Next,
        /// <summary>지정 행동(`allow`)이 받아들여지면. 자동 진행은 `auto`를 낸다.</summary>
        Command,
        /// <summary>봇 좌석 `BotSeat`의 답 `BotAnswers`개(봇 턴 보기). 명령을 받지 않는다.</summary>
        Bot,
        /// <summary>자유(아무 명령이나 — 엔진 합법 답 그대로). 진행기에 끝을 알리면 넘어간다.</summary>
        Free,
        /// <summary>마박사가 대신 둔다(막 맛보기, 07 T19): 사람 명령은 모두 막고, 진행기(<see cref="TutorialRunner.Play"/>)가 `auto`를 내 좌석으로 낸다.</summary>
        Demo,
        /// <summary>화면 `Screen`이 열리면(`screen:&lt;이름&gt;`): 명령을 받지 않는다. 화면 쪽이 그 화면을 열 때 Finish — 자동 진행은 바로 넘어간다.</summary>
        Screen,
    }

    /// <summary>엔진 값 대조 한 줄(07 §7 `expect`). 키는 <see cref="TutorialExpect"/>.</summary>
    public sealed class ExpectValue
    {
        public string Key;
        public int Value;

        public ExpectValue(string key, int value)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Value = value;
        }

        public override string ToString() => $"{Key}={Value}";
    }

    /// <summary>각본 단계 하나(07 §7). 대사·앵커·근거는 화면·데이터 검사가 쓰고, 진행기는 advance·allow·auto·dice·expect를 쓴다.</summary>
    public sealed class TutorialStep
    {
        public string Id;
        public int Chapter;
        /// <summary>대사 문자열 키(`tutorial.&lt;각본&gt;.&lt;단계&gt;`). null = 말없는 단계(막 앞 판 각본의 판 차리기 — 화면이 빨리감기로 지난다, 07 T160).</summary>
        public string Text;
        /// <summary>거르개가 막았거나 구멍 밖을 눌렀을 때 띄우는 대사 키(`….hint`). null = 없음.</summary>
        public string Hint;
        /// <summary>가리킬 화면 앵커(요소 name · `cell:n[,n…]` · `ds:요소`). null = 가운데.</summary>
        public string Anchor;
        public StepAdvance Advance;
        /// <summary>Screen 단계가 기다리는 화면 이름(그 밖은 null).</summary>
        public string Screen;
        public int BotSeat = -1;
        public int BotAnswers;
        /// <summary>이 단계에서 받는 내 명령(부분 일치, 하나라도 맞으면). Command 단계에서 비우면 아무것도 받지 않는다.</summary>
        public List<AnswerPattern> Allow = new List<AnswerPattern>();
        /// <summary>자동 진행(시험·빨리감기)이 낼 내 답 — 차례로, 각각 내 열린 선택의 합법 답에서 고른다.</summary>
        public List<AnswerPattern> Auto = new List<AnswerPattern>();
        /// <summary>이 단계에서 굴릴 주사위 면(ForcedDice). 단계 끝에 남으면 실패.</summary>
        public List<AttackDieFace> Dice = new List<AttackDieFace>();
        public List<ExpectValue> Expect = new List<ExpectValue>();
        /// <summary>대사 근거(rulings ID · 01 절 · 룰북 쪽).</summary>
        public List<string> Ref = new List<string>();
    }

    /// <summary>덱 윗장 고정 한 줄(<see cref="TutorialDeal.Top"/>).</summary>
    public sealed class DeckTop
    {
        public DeckId Deck;
        public string[] Keys;
    }

    /// <summary>숨겨진 장소 정체 고정 한 줄(<see cref="TutorialDeal.HiddenSpot"/>).</summary>
    public sealed class SpotFix
    {
        public int Cell;
        public string Item;
    }

    /// <summary>판 차리기(07 §7 `start`): 캠페인·인원·시드·규칙 옵션 + 정해진 판.</summary>
    public sealed class TutorialStart
    {
        public Campaign Campaign = Campaign.HeartOfSinnoh;
        public int Seats = 1;
        public ulong Seed;
        public RuleOptions Options = new RuleOptions();
        /// <summary>좌석마다 트레이너 카드 3장(null = 시드대로).</summary>
        public string[][] TrainerCards;
        public List<DeckTop> Tops = new List<DeckTop>();
        public List<SpotFix> Spots = new List<SpotFix>();
        /// <summary>
        /// 막 앞 판 손보기의 이름(07 §4-1·§4-3 — Ai의 막 길 `SoloTrail.After` 꼴). null = 없음. Rules는 Ai를 모르므로 이름만 적고,
        /// 조립 지점(화면·시험)이 이름 → 손보기 함수를 <see cref="TutorialRunner"/>에 꽂는다. 셋업이 끝난 뒤 첫 단계 끝에 한 번 건다.
        /// </summary>
        public string After;

        public TutorialDeal BuildDeal(Ctx ctx)
        {
            var d = new TutorialDeal(ctx);
            foreach (var t in Tops) d.Top(t.Deck, t.Keys);
            if (TrainerCards != null)
            {
                if (TrainerCards.Length != Seats) throw new ArgumentException($"트레이너 카드 {TrainerCards.Length}좌석 ≠ 인원 {Seats}");
                d.TrainerCards(TrainerCards);
            }
            foreach (var s in Spots) d.HiddenSpot(s.Cell, s.Item);
            return d;
        }
    }

    /// <summary>각본 하나 = 본편 또는 확장 장 하나(07 §7). 좌석 0 = 나, 나머지는 봇(각본 큐).</summary>
    public sealed class TutorialScript
    {
        public string Id;
        public TutorialStart Start = new TutorialStart();
        /// <summary>좌석마다 봇 각본 큐(좌석 0은 늘 null). 길이 = 인원(null이면 봇 없음).</summary>
        public List<AnswerPattern>[] Bots;
        public List<TutorialStep> Steps = new List<TutorialStep>();
    }
}
