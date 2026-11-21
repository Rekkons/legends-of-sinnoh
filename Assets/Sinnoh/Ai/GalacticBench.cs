using System;
using System.Collections.Generic;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Ai
{
    /// <summary>시험대 장면(01 §9-7): 조무래기(1막, 2마리) · 간부(2막, 5마리 중 조각 수 + 2) · 태홍(3막, 8마리 중 6).</summary>
    public enum BenchScene { Grunt, Commander, Cyrus }

    /// <summary>시험대 결과(장면 하나 × 시드 여럿). 시간은 밖에서 준 시계로만 잰다(없으면 0).</summary>
    public sealed class BenchResult
    {
        public int Battles, GalacticWins, PlayerWins, Draws;
        /// <summary>끝까지 가지 못한 배틀(있으면 안 된다).</summary>
        public int Stuck;
        /// <summary>배틀이 끝났을 때 플레이어 파티의 남은 체력 비율(천분율) 합(배틀 수로 나누면 평균).</summary>
        public long PlayerHpLeft;
        /// <summary>갤럭시단이 고른 결정 수(합법 답이 둘 이상인 것)와 넣은 답 전체 수.</summary>
        public long Decisions, Steps;
        /// <summary>갤럭시단 결정에 든 시간 합·최댓값(ms).</summary>
        public long ThinkMs, MaxThinkMs;

        /// <summary>갤럭시단 승률(천분율 — 비김은 반 승).</summary>
        public int WinPermille => Battles > 0 ? (2 * GalacticWins + Draws) * 500 / Battles : 0;
        /// <summary>플레이어 파티의 평균 남은 체력(천분율).</summary>
        public int PlayerHpPermille => Battles > 0 ? (int)(PlayerHpLeft / Battles) : 0;

        /// <summary>다른 결과를 더한다(장면 종류별 결과를 합계로 — 시간 최댓값은 큰 쪽).</summary>
        public void Add(BenchResult o)
        {
            Battles += o.Battles;
            GalacticWins += o.GalacticWins;
            PlayerWins += o.PlayerWins;
            Draws += o.Draws;
            Stuck += o.Stuck;
            PlayerHpLeft += o.PlayerHpLeft;
            Decisions += o.Decisions;
            Steps += o.Steps;
            ThinkMs += o.ThinkMs;
            if (o.MaxThinkMs > MaxThinkMs) MaxThinkMs = o.MaxThinkMs;
        }

        /// <summary>천분율을 「12.3%」 꼴로.</summary>
        public static string Percent(long permille) => $"{permille / 10}.{permille % 10}%";

        public override string ToString() =>
            $"배틀 {Battles} · 갤럭시단 승 {GalacticWins} · 비김 {Draws} · 패 {PlayerWins} (승률 {Percent(WinPermille)}) · 플레이어 남은 체력 {Percent(PlayerHpPermille)} · 결정 {Decisions}"
            + (ThinkMs > 0 ? $" · 결정당 {(Decisions > 0 ? ThinkMs * 10 / Decisions / 10 : 0)}.{(Decisions > 0 ? ThinkMs * 10 / Decisions % 10 : 0)}ms(최대 {MaxThinkMs}ms)" : "") + (Stuck > 0 ? $" · 멈춤 {Stuck}" : "");
    }

    /// <summary>
    /// AI 시험대(01 §9-7 밑작업 ⑧, M8): 무작위 장면(플레이어 파티를 덱에서 시드로 뽑는다) × 시드에서 갤럭시단 AI와 고정 플레이어 봇(p.25 원칙)을 맞붙인다.
    /// 엔진 무관(dotnet·Unity 같은 결과) — 같은 시드·같은 AI면 같은 숫자다. 갤럭시단 AI는 관전자 보기만 받는다.
    /// 장면의 플레이어 파티 [가정 M8-4]: 열린 칸을 모두 채운다 — 조무래기 = 스타팅 포켓몬 1 + 약한 덱, 간부 = 보통 덱(가진 조각 = 시드 % 3), 태홍 = 강한 덱·진화 덱 번갈아.
    /// 기술 카드·지닌물건·트레이너 카드·운명 손패는 없다(시그니처 기술만).
    /// </summary>
    public static class GalacticBench
    {
        /// <summary>
        /// 합격선(01 §9-7 「원칙 AI보다 10%p」, 천분율). 어디서 세나 [가정 M8-8]: 실전 장면 정본 1,000개의 **전체 합계** 승률 차 — 장면 종류(조무래기·간부·태홍)마다의 차는 숫자만 보고한다.
        /// </summary>
        public const int PassMargin = 100;

        /// <summary>장면 하나: 갤럭시단 배틀의 첫 선택(조무래기 = 출전, 간부·태홍 = 명단)이 열린 호스트 원본.</summary>
        public static GameState Scene(Ctx ctx, BenchScene scene, ulong seed)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            var s = Engine.NewGame(ctx, new RuleOptions(), 1, seed);
            s.Phase = Phase.Turn;
            s.Round = 1;
            s.TurnId = 1;
            s.ActiveSeat = 0;
            var st = s.Seats[0];
            var fixtures = ctx.Fixtures.Fixtures;
            for (int f = 0; f < fixtures.Count; f++)
            {
                if (fixtures[f].Kind != FixtureKind.Village || ctx.Board[fixtures[f].Cell].DistortionWorld) continue;
                st.HomeVillage = f;
                st.Cell = fixtures[f].Cell;
                break;
            }
            int held = scene == BenchScene.Grunt ? 0 : scene == BenchScene.Commander ? (int)(seed % 3) : SeatState.ShardCount;
            if (scene != BenchScene.Grunt)
            {
                var reveal = new Emitter(s, null);
                while (s.Hos.Act < (scene == BenchScene.Cyrus ? 3 : 2)) SetupFlow.RevealAct(s, ctx, reveal, -1);
                st.ActProgress = held == SeatState.ShardCount ? 2 : 1;
                s.Hos.FirstSeat[1] = 0;
                if (held == SeatState.ShardCount) s.Hos.FirstSeat[2] = 0;
                for (int i = 0; i < held; i++) st.Shards[i] = true;
            }
            st.OpenSlots = CampaignRules.OpenPartySlots(s.Campaign, held);
            for (int i = 0; i < st.OpenSlots; i++)
            {
                var deck = scene == BenchScene.Grunt ? (i == 0 ? DeckId.Starter : DeckId.Weak)
                    : scene == BenchScene.Commander ? DeckId.Moderate
                    : i % 2 == 0 ? DeckId.Strong : DeckId.Evolution;
                var pile = s.Decks[(int)deck];
                int at = deck == DeckId.Starter ? s.Rng.Next(pile.Count) : pile.Count - 1; // 스타팅 덱은 섞지 않는다
                int card = pile[at];
                pile.RemoveAt(at);
                PokemonRules.AddToParty(st, PokemonRules.Spawn(s, card, 0, (int)deck));
            }

            var fx = new Emitter(s, null);
            fx.Begin(0, ActionKind.Challenge, AutoKind.Auto);
            switch (scene)
            {
                case BenchScene.Grunt:
                    BattleFlow.StartGalactic(s, ctx, fx, 0);
                    break;
                case BenchScene.Commander:
                    st.Cell = HosFlow.LakeCell(ctx, (Shard)held);
                    HosFlow.OpenLineup(s, ctx, fx, 0, HosFlow.CommanderOf((Shard)held));
                    break;
                default:
                    st.Heart = true;
                    st.Cell = HosFlow.TempleCell(ctx);
                    HosFlow.OpenLineup(s, ctx, fx, 0, GalacticTrainer.Cyrus);
                    break;
            }
            return s;
        }

        /// <summary>
        /// 장면 × 시드 0..seeds−1(firstSeed부터). galactic = 갤럭시단 AI(관전자 보기로 묻는다), 플레이어 = 원칙 봇. maxSimulations·aiSeed = AI 예산(선택마다 씨를 섞는다).
        /// now = 결정 시간을 잴 시계(ms, 엔진 밖에서 준다 — 없으면 재지 않는다).
        /// </summary>
        public static BenchResult Run(Ctx ctx, BenchScene scene, int seeds, IAgent galactic, int maxSimulations = 0, ulong aiSeed = 1, Func<long> now = null, ulong firstSeed = 0)
        {
            if (galactic == null) throw new ArgumentNullException(nameof(galactic));
            var result = new BenchResult();
            for (ulong seed = firstSeed; seed < firstSeed + (ulong)seeds; seed++)
                Play(Scene(ctx, scene, seed), ctx, galactic, Player, result, maxSimulations, aiSeed, seed, now);
            return result;
        }

        /// <summary>플레이어 쪽 고정 봇: p.25 원칙(<see cref="PrincipleAgent.Choose"/>).</summary>
        public static readonly RolloutPolicy Player = (g, cx, c, legal) => PrincipleAgent.Choose(g, cx, c, legal).Answer;

        /// <summary>
        /// 플레이어 쪽을 AI로(M8 「시험대 바로잡기」 — 결정 108 ①): 갤럭시단 배틀 안의 좌석 선택은 agent에게 **그 좌석의 보기**(<see cref="Engine.ViewFor"/> — 엿보기 없음)로 묻고, 그 밖은 fallback.
        /// 예산 씨는 aiSeed · gameSeed · 선택 Id를 섞는다(갤럭시단 쪽과 다른 aiSeed를 준다). 답이 합법 답이 아니면 예외.
        /// </summary>
        public static RolloutPolicy SeatAgent(IAgent agent, RolloutPolicy fallback, int maxSimulations = 0, ulong aiSeed = 2, ulong gameSeed = 0)
        {
            if (agent == null) throw new ArgumentNullException(nameof(agent));
            if (fallback == null) throw new ArgumentNullException(nameof(fallback));
            var noLog = new List<GameEvent>();
            return (g, cx, c, legal) =>
            {
                if (c.Seat < 0 || g.Battle == null || g.Battle.Kind != BattleKind.Galactic) return fallback(g, cx, c, legal);
                var view = Engine.ViewFor(g, c.Seat);
                var input = new AgentInput { View = view, Choice = view.Pending.Find(x => x.Id == c.Id), Ctx = cx, PublicLog = noLog };
                var budget = new AgentBudget { MaxSimulations = maxSimulations, Seed = SampledRng.Mix(aiSeed ^ (gameSeed << 20) ^ (ulong)c.Id) };
                var d = agent.Decide(input, budget);
                foreach (var a in legal)
                    if (a.SameAs(d.Answer)) return d.Answer;
                throw new InvalidOperationException($"판 {gameSeed}: 좌석 {c.Seat} AI 답 {d.Answer}가 {c.Kind}/{c.Sub}의 합법 답이 아니다");
            };
        }

        /// <summary>장면의 주사위만 바꾼 사본(팽팽한 장면 고르기 — 덱 순서 · 숨은 정보는 그대로, 게임 난수만 dice에서 새로). dice 0 = 원본 그대로.</summary>
        public static GameState Reroll(GameState scene, ulong sceneId, int dice)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            var s = scene.Clone();
            if (dice > 0) SampledRng.ForSample(SampledRng.Mix(sceneId ^ 0xD1CE), dice).Seed(s);
            return s;
        }

        /// <summary>팽팽한 장면의 갤럭시단 승률 구간(‰, 원칙 AI끼리 여러 주사위) [가정 01 §13-1 M8-11 — 결정 108 ①].</summary>
        public const int TightLow = 300, TightHigh = 700;

        /// <summary>
        /// 배틀 하나(갤럭시단 배틀의 선택이 열린 호스트 원본 s — 그 자리에서 고친다)를 끝까지 돌려 result에 더한다. 갤럭시단 선택은 galactic에게 관전자 보기로 묻고, 그 밖의 선택은 player가 답한다.
        /// gameSeed = 이 판을 가리는 값(AI 씨에 섞는다 — 게임 난수와 무관).
        /// </summary>
        public static void Play(GameState s, Ctx ctx, IAgent galactic, RolloutPolicy player, BenchResult result, int maxSimulations = 0, ulong aiSeed = 1, ulong gameSeed = 0, Func<long> now = null)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (galactic == null) throw new ArgumentNullException(nameof(galactic));
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (result == null) throw new ArgumentNullException(nameof(result));
            var noLog = new List<GameEvent>();
            RolloutPolicy policy = (g, cx, c, legal) =>
            {
                if (c.Seat != SeatId.Galactic) return player(g, cx, c, legal);
                var view = Engine.ViewFor(g, SeatId.Observer);
                var input = new AgentInput { View = view, Choice = view.Pending.Find(x => x.Id == c.Id), Ctx = cx, PublicLog = noLog };
                var budget = new AgentBudget { MaxSimulations = maxSimulations, Seed = SampledRng.Mix(aiSeed ^ (gameSeed << 20) ^ (ulong)c.Id) };
                long t0 = now != null ? now() : 0;
                var d = galactic.Decide(input, budget);
                if (now != null)
                {
                    long ms = now() - t0;
                    result.ThinkMs += ms;
                    if (ms > result.MaxThinkMs) result.MaxThinkMs = ms;
                }
                if (legal.Count > 1) result.Decisions++;
                foreach (var a in legal)
                    if (a.SameAs(d.Answer)) return d.Answer;
                throw new InvalidOperationException($"판 {gameSeed}: AI 답 {d.Answer}가 {c.Kind}/{c.Sub}의 합법 답이 아니다");
            };
            var r = Rollout.ToEnd(s, ctx, policy);
            result.Battles++;
            result.Steps += r.Steps;
            if (!r.Ended)
            {
                result.Stuck++;
                return;
            }
            if (r.Winner == 1) result.GalacticWins++;
            else if (r.Winner == 0) result.PlayerWins++;
            else result.Draws++;
            result.PlayerHpLeft += r.HpPermille(0);
        }
    }
}
