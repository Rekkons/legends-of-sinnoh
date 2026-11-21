using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M9 실행 감사 ③ 트레이너 능력 충돌 쌍(<see cref="ExecAuditPairs"/>). 관문에는 51쌍(카드마다 공격하는 쪽 한 번), 전체 51 × 51쌍은 [Explicit] → <c>Logs/Review/m9/exec-audit/pairs.tsv</c>(git 밖).
    /// 순서 · 한도 · 둘째 바퀴는 의심 목록(보고 줄)이고, 예외 · 합법 밖 기본 답 · 멈춤만 셈해 보고한다.
    /// </summary>
    public class ExecAuditPairTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static void Report(string line)
        {
            TestContext.WriteLine(line);
            TestContext.Progress.WriteLine(line);
        }

        static void Summary(List<ExecAuditPairs.Pair> pairs, Func<int, string> key, string head)
        {
            int errors = 0, order = 0, limit = 0, noLap = 0, together = 0, laps = 0, moments = 0;
            var met = new HashSet<string>();
            foreach (var p in pairs)
            {
                if (p.Error != null) errors++;
                if (p.Order.Count > 0) order++;
                if (p.Limit.Count > 0) limit++;
                if (p.NoLap.Count > 0) noLap++;
                together += p.Together;
                laps += p.Laps;
                moments += p.Moments;
                foreach (var m in p.Met) met.Add(m);
            }
            Report($"[{head}] {pairs.Count}쌍 · 같은 묶음에서 같이 씀 {together} · 같은 순간 둘 이상 씀 {moments} ·다시 물은 바퀴 {laps} · 같이 쓴 출처 쌍 {met.Count} · 오류 {errors} · 순서 의심 {order} · 한도 의심 {limit} · 둘째 바퀴 의심 {noLap}");
            foreach (var p in pairs)
            {
                string name = $"{key(p.A)} 대 {key(p.B)} (밖 {key(p.C)})";
                if (p.Error != null) Report($"  오류 {name}: {p.Error}");
                foreach (var o in p.Order) Report($"  순서 {name}: {o}");
                foreach (var l in p.Limit) if (!l.EndsWith("(정보)")) Report($"  한도 {name}: {l}");
            }
        }

        [Test]
        public void M9_실행감사_능력충돌쌍_작은판()
        {
            int n = ctx.Cards.TrainerCards.Count;
            var pairs = new List<ExecAuditPairs.Pair>();
            for (int k = 0; k < n; k++) pairs.Add(ExecAuditPairs.Run(ctx, k, (k * 7 + 3) % n, (k * 13 + 5) % n, (ulong)(k + 1)));
            Summary(pairs, c => ctx.Cards.TrainerCards[c].Key, "M9 능력 충돌 쌍 작은 판");
            Assert.AreEqual(n, pairs.Count);
            long commands = 0, groups = 0;
            foreach (var p in pairs)
            {
                commands += p.Commands;
                groups += p.Groups;
            }
            Assert.Greater(commands, n * 5, "배틀이 실제로 돌았다");
            Assert.Greater(groups, 0, "여러 좌석에 한꺼번에 묻는 반응 묶음이 나왔다");
            Assert.AreEqual(n + 1, ExecAuditPairs.Tsv(ctx, pairs).TrimEnd('\n').Split('\n').Length, "머리 + 쌍마다 한 줄");
        }

        [Test, Explicit("오래 걸린다 — 손으로 돌리는 실행 감사"), Category("Slow")]
        public void M9_실행감사_능력충돌쌍_전체()
        {
            int n = ctx.Cards.TrainerCards.Count;
            var pairs = new List<ExecAuditPairs.Pair>();
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                    pairs.Add(ExecAuditPairs.Run(ctx, a, b, (a + b + 1) % n, (ulong)(a * n + b + 1)));
            Summary(pairs, c => ctx.Cards.TrainerCards[c].Key, "M9 능력 충돌 쌍 전체");
            string dir = Path.Combine(TestData.RepoRoot(), "Logs", "Review", "m9", "exec-audit");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "pairs.tsv"), ExecAuditPairs.Tsv(ctx, pairs), new System.Text.UTF8Encoding(false));
        }

        [Test]
        public void M9_결정131_초능력자빨강과디아루가는굴린뒤같은반응묶음에서공격자먼저_한바퀴더()
        {
            // 결정 131 뒤 기대 동작(rulings G6): 굴린 직후 열리는 반응은 **한 묶음**(같은 GroupId)이고 공격하는 좌석의 psychic R(Source = 카드) · 받는 좌석의 운명 창이 같이 있다.
            // 둘 다 쓰면 공격하는 쪽 → 받는 쪽 순서로 적용(G6 — Order 비어 있음), 누가 썼으면 같은 창(AfterRoll)에서 쓸 수 있는 좌석에 한 바퀴 더 묻는다.
            // 이 판의 「쓸 수 있으면 쓴다」 정책은 공격자가 첫 바퀴에 psychic R을 쓰므로, 받는 쪽이 디아루가를 낸 뒤 공격자가 둘째 바퀴에 받아치는 길은 장면 시험(TrainerBattleTests G6_결정131_… 둘)이 본다.
            // 좌석의 반응은 선택 하나(G6 「한 좌석의 둘은 한 선택의 답」)라 쓸 수 있는 운명 카드를 쥔 좌석의 선택 출처는 Fate다 — 출처로 psychic R을 가려 보려고 공격하는 좌석에는 운명 카드를 주지 않는다.
            // 이 판에서 에스퍼 · 고스트 기술로 공격하는 쪽은 좌석 1(고스트 파티)이다 — 좌석 0의 에스퍼 포켓몬은 다른 타입 기술로 공격한다.
            int psychic = ctx.Cards.TrainerCardIds.IndexOf("psychic");
            int laps = 0;
            ulong at = 0; // 운명 손패는 덱 위 2장 — 시드를 돌려 받는 쪽이 디아루가를 쥔 판을 찾는다
            for (ulong seed = 1; seed < 200 && at == 0; seed++)
            {
                var p = ExecAuditPairs.Run(ctx, psychic, psychic, -1, seed, fateless: 1);
                foreach (var m in p.Met)
                    if (m.Contains($"1:{SourceKind.TrainerCard}:{psychic}") && m.Contains($"0:{SourceKind.Fate}")) at = seed;
                Assert.IsEmpty(p.Order, $"시드 {seed}: G6 순서(공격하는 쪽 먼저)");
                Assert.IsNull(p.Error, $"시드 {seed}");
                laps += p.Laps;
            }
            Report($"[M9 결정 131] psychic R(좌석 1 공격) + 운명(좌석 0 받는 쪽) 같은 묶음: 시드 {at} · 그때까지 다시 물은 바퀴 {laps}");
            Assert.AreNotEqual(0UL, at, "psychic R(공격하는 좌석 1)과 운명 창(받는 좌석 0)이 같은 반응 묶음에서 같이 쓰였다");
            Assert.Greater(laps, 0, "누가 쓰면 같은 창에서 한 바퀴 더 묻는다");
        }
    }
}
