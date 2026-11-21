using System;
using System.Collections.Generic;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Ai
{
    /// <summary>사람 기록을 건너뛴 까닭(결정 113 — 「못 읽음」으로 센다).</summary>
    public enum HumanSkip
    {
        None,
        /// <summary>머리의 형식 판이 이 빌드와 다르다(옛 저장 꼴).</summary>
        Format,
        /// <summary>규칙 판이 다르다 — 처음부터 다시 적용하면 어긋날 수 있다.</summary>
        Rules,
        /// <summary>데이터 해시 · 데이터 표(IdTables)가 다르다.</summary>
        Data,
        /// <summary>다시 적용하다 명령이 거절됐다(Seq가 이어지지 않음 · 거절).</summary>
        Replay,
    }

    /// <summary>
    /// 저장 하나에서 뽑은 갤럭시단 배틀 하나(결정 113): 배틀 첫 선택 자리의 호스트 원본(<see cref="Scene"/> — 조무래기 = 배틀이 열린 자리, 간부 · 태홍 = 명단 고르기 — 시험대 실전 장면과 같은 자리) +
    /// 그 자리부터 배틀이 끝날 때까지 실제로 들어간 명령(<see cref="Lines"/> — 장면 사본에 차례로 다시 적용하면 실제 배틀) + 그 가운데 사람 답 자리(<see cref="Human"/>).
    /// </summary>
    public sealed class HumanBattle
    {
        public GameState Scene;
        /// <summary>판 안 번호(나온 순서).</summary>
        public int Index;
        /// <summary>도전한 좌석(배틀 Side 0).</summary>
        public int Seat;
        public readonly List<SaveLogLine> Lines = new List<SaveLogLine>();
        /// <summary><see cref="Lines"/> 안 사람 답의 자리: 도전한 좌석이 그 좌석의 선택에 낸 답(<see cref="CommandKind.Answer"/>, AI 답 아님 — 시간 초과 · 정비 · AI 좌석 답은 뺀다).</summary>
        public readonly List<int> Human = new List<int>();
        /// <summary>실제로 배틀이 끝났나(저장이 배틀 한가운데서 끝나면 false) · 이긴 쪽(배틀 Side — 1 갤럭시단 · 0 도전자 · −1 비김 · 도주).</summary>
        public bool Ended;
        public int Winner = -1;
    }

    /// <summary>저장 하나를 읽은 결과: 건너뛰었으면 <see cref="Skip"/> · <see cref="Detail"/>(배틀은 비어 있다).</summary>
    public sealed class HumanGame
    {
        public HumanSkip Skip;
        public string Detail;
        public readonly List<HumanBattle> Battles = new List<HumanBattle>();
        /// <summary>다시 적용한 명령 수 · 뽑은 사람 답 수.</summary>
        public int Commands, HumanMoves;
    }

    /// <summary>
    /// 사람 기록 → 시험대(M8, 결정 113): 저장 파일의 머리 + 명령 기록(시드에서 처음부터 다시 적용 — 01 §10)을 **읽기만** 해서 갤럭시단 배틀 장면과 그 배틀에서 사람이 고른 수를 뽑는다.
    /// 파일을 읽는 일은 부르는 쪽(Data SaveStore — 시험 쪽 진입점)이 하고, 여기는 이미 읽은 머리 · 줄만 받는다(엔진 무관). 옛 저장 꼴(형식 판 · 규칙 판 · 데이터가 다름)과
    /// 다시 적용이 어긋난 저장은 던지지 않고 <see cref="HumanGame.Skip"/>으로 돌려준다. 장면 고르는 규칙은 시험대 실전 장면(봇 판의 RealScenes)과 같다 [가정 01 §13-1 M8-30].
    /// </summary>
    public static class HumanRecord
    {
        /// <summary>
        /// 저장 하나(머리 + log.jsonl 전체)를 읽는다. contentHash = 지금 데이터 해시(null이면 해시는 견주지 않고 데이터 표만 본다).
        /// </summary>
        public static HumanGame Read(Ctx ctx, SaveHead head, IReadOnlyList<SaveLogLine> lines, string contentHash = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (head == null) throw new ArgumentNullException(nameof(head));
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            var game = new HumanGame();
            if (head.FormatVersion != SaveRecord.CurrentFormat) return Skipped(game, HumanSkip.Format, $"형식 {head.FormatVersion}(이 빌드 {SaveRecord.CurrentFormat})");
            if (head.RulesVersion != SaveRecord.CurrentRulesVersion) return Skipped(game, HumanSkip.Rules, $"규칙 판 {head.RulesVersion}(이 빌드 {SaveRecord.CurrentRulesVersion})");
            if (contentHash != null && head.ContentHash != contentHash) return Skipped(game, HumanSkip.Data, $"데이터 해시 {head.ContentHash}(지금 {contentHash})");
            string diff = SaveRecord.IdTablesDiff(head.IdTables, SaveRecord.IdTablesOf(ctx));
            if (diff != null) return Skipped(game, HumanSkip.Data, diff);
            if (head.Options == null || head.SeatCount < 1 || head.SeatCount > SeatId.MaxSeats) return Skipped(game, HumanSkip.Data, "머리의 규칙 옵션 · 좌석 수");

            var s = Engine.NewGame(ctx, head.Options, head.SeatCount, head.Seed, head.Campaign);
            Engine.Start(s, ctx, null);
            var watch = new EndWatch();
            HumanBattle open = null;
            int lastBattle = -1, lastLineup = -1, skip = -1;
            foreach (var line in lines)
            {
                if (line?.Command == null || line.Seq != s.CommandCount) return Skipped(game, HumanSkip.Replay, $"로그 Seq {line?.Seq} ≠ CommandCount {s.CommandCount}");
                bool human = false;
                if (open != null && line.Seat == open.Seat && line.Command.Kind == CommandKind.Answer && !line.Command.ByAi)
                {
                    var c = ChoiceRules.Find(s, line.Command.ChoiceId);
                    human = c != null && !c.Answered && c.Seat == line.Seat;
                }
                var r = Engine.Submit(s, ctx, line.Seat, line.Command, open != null ? watch : null);
                if (!r.Accepted) return Skipped(game, HumanSkip.Replay, $"로그 {line.Seq}번 명령이 거절됐다({r})");
                game.Commands++;
                if (open != null)
                {
                    open.Lines.Add(line);
                    if (human)
                    {
                        open.Human.Add(open.Lines.Count - 1);
                        game.HumanMoves++;
                    }
                    if (watch.Ended)
                    {
                        open.Ended = true;
                        open.Winner = watch.Winner;
                        open = null;
                    }
                }

                // 장면 고르기(시험대 RealScenes와 같은 규칙): 명단 고르기가 열린 자리 · 명단 없이 열린 갤럭시단 배틀의 첫 자리
                var lineup = s.Pending.Find(x => x.Kind == ChoiceKind.Lineup && !x.Answered);
                int seat, battle;
                if (lineup != null)
                {
                    if (lineup.Id == lastLineup) continue;
                    lastLineup = lineup.Id;
                    skip = s.NextBattleId; // 이 명단으로 열릴 배틀은 다시 담지 않는다
                    seat = lineup.Subject;
                    battle = skip;
                }
                else
                {
                    var b = s.Battle;
                    if (b == null || b.Kind != BattleKind.Galactic || b.Id == lastBattle) continue;
                    lastBattle = b.Id;
                    if (b.Id == skip) continue;
                    seat = b.Sides[0].Owner;
                    battle = b.Id;
                }
                open = new HumanBattle { Scene = s.Clone(), Index = game.Battles.Count, Seat = seat };
                game.Battles.Add(open);
                watch.Battle = battle;
                watch.Ended = false;
            }
            return game;
        }

        static HumanGame Skipped(HumanGame game, HumanSkip why, string detail)
        {
            game.Skip = why;
            game.Detail = detail;
            game.Battles.Clear();
            game.HumanMoves = 0;
            return game;
        }

        /// <summary>그 배틀(Id)이 닫히는 자리만 받는다(이벤트를 만들지 않는다 — Rollout과 같은 길).</summary>
        sealed class EndWatch : IBattleEndWatch
        {
            public int Battle = -1;
            public bool Ended;
            public int Winner = -1;

            public void Add(GameEvent e) { }

            public void BattleOver(GameState s, BattleState b, BattleResult result, int winner)
            {
                if (b.Id != Battle || Ended) return;
                Ended = true;
                Winner = winner;
            }
        }
    }
}
