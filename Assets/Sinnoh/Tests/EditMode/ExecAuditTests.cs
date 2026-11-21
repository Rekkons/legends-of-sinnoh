using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M9 실행 감사 ① 능력 발동 표(<see cref="ExecAudit"/>). 전체 판은 [Explicit](관문 밖 — 손으로: dotnet <c>--filter M9_실행감사_능력발동표_전체</c>,
    /// 판 수 SINNOH_EXEC_AUDIT_GAMES = 심장 판 수 · UB는 세 배, 기본 102 — 심장 판당 약 6초) → <c>Logs/Review/m9/exec-audit/abilities.tsv</c>(git 밖). 관문에는 작은 판 둘(표가 깨지지 않는지만).
    /// 좌석 카드는 판을 넘어 51장을 차례로 돌린다(4~6인 판 · 판마다 좌석 수만큼 다음 카드). 신오의 심장은 그대로 해금, 울트라 버스트는 첫 맵 턴에 골드로 풀어 준다(판이 짧다).
    /// </summary>
    public class ExecAuditTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static void Report(string line)
        {
            TestContext.WriteLine(line);
            TestContext.Progress.WriteLine(line);
        }

        /// <summary>캠페인마다 games판: 좌석 수 4 + 판 % 3, 카드는 next부터 차례로.</summary>
        static void Sweep(Ctx ctx, ExecAudit.Table t, Campaign campaign, int games, ulong seedBase, int maxRounds)
        {
            int n = ctx.Cards.TrainerCards.Count, next = 0;
            for (int gi = 0; gi < games; gi++)
            {
                int seats = 4 + gi % 3;
                var cards = new int[seats];
                for (int i = 0; i < seats; i++) cards[i] = next++ % n;
                bool ub = campaign == Campaign.UltraBurst;
                ExecAudit.Game(ctx, t, seats, seedBase + (ulong)gi, cards, campaign, ub, maxRounds, ub ? new RuleOptions { UltraOnslaught = gi % 2 == 1 } : null);
            }
        }

        [Test]
        public void M9_실행감사_능력발동표_작은판()
        {
            // 관문용: 심장 6인 12라운드까지 · UB 6인(첫 턴 골드) 8라운드까지 — 표가 102줄로 나오고 예외가 없고 UB 판에서 능력이 제안·사용된다
            var t = ExecAudit.NewTable(ctx);
            ExecAudit.Game(ctx, t, 6, 1, new[] { 0, 1, 2, 3, 4, 5 }, Campaign.HeartOfSinnoh, false, 12);
            ExecAudit.Game(ctx, t, 6, 1, new[] { 6, 7, 8, 9, 10, 11 }, Campaign.UltraBurst, true, 8);
            Assert.IsEmpty(t.Errors, string.Join("\n", t.Errors));
            Assert.AreEqual(2, t.Games);
            Assert.AreEqual(ctx.Cards.TrainerCards.Count * 2, t.Rows.Length, "능력 102개");
            var lines = ExecAudit.Tsv(t).TrimEnd('\n').Split('\n');
            Assert.AreEqual(t.Rows.Length + 1, lines.Length, "머리 + 능력마다 한 줄");
            for (int k = 0; k < 6; k++) Assert.AreEqual(1, t.Of(k, ExecAudit.Blue).Seats, $"{t.Of(k, 0).Card}: 정해 준 좌석이 그 카드를 골랐다");
            for (int k = 6; k < 12; k++) Assert.AreEqual(1, t.Of(k, ExecAudit.Red).Unlocked, $"{t.Of(k, 0).Card}: UB 판 첫 턴에 빨강까지 풀었다");
            long seen = 0;
            for (int k = 12; k < 24; k++) seen += t.Rows[k].Offered + t.Rows[k].Used + t.Rows[k].Chosen;
            Assert.Greater(seen, 0, "골드로 푼 카드 여섯의 능력이 제안 · 사용으로 보인다");
        }

        [Test, Explicit("오래 걸린다 — 손으로 돌리는 실행 감사"), Category("Slow")]
        public void M9_실행감사_능력발동표_전체()
        {
            int games = int.TryParse(Environment.GetEnvironmentVariable("SINNOH_EXEC_AUDIT_GAMES"), out var g) && g > 0 ? g : 102;
            var sw = Stopwatch.StartNew();
            var t = ExecAudit.NewTable(ctx);
            Sweep(ctx, t, Campaign.HeartOfSinnoh, games, 1000, 0);
            long hosMs = sw.ElapsedMilliseconds;
            Sweep(ctx, t, Campaign.UltraBurst, games * 3, 2000, 0); // UB 판은 짧다(판당 약 0.1초) — 세 배
            sw.Stop();
            string dir = Path.Combine(TestData.RepoRoot(), "Logs", "Review", "m9", "exec-audit");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "abilities.tsv");
            File.WriteAllText(path, ExecAudit.Tsv(t), new System.Text.UTF8Encoding(false));
            Report($"[M9 실행 감사 능력 발동 표] 심장 {games}판({hosMs}ms) + UB {games * 3}판({sw.ElapsedMilliseconds - hosMs}ms) · 끝난 판 {t.Games} · 명령 {t.Commands} · 오류 {t.Errors.Count} → {path}");
            foreach (var e in t.Errors) Report("  오류 " + e);
            foreach (var r in t.Rows)
            {
                string v = ExecAudit.Verdict(r);
                if (v != "ok") Report($"  {r.Card} {(r.Color == ExecAudit.Blue ? "B" : "R")}: {v} (좌석 {r.Seats} · 풀림 {r.Unlocked} · 제안 {r.Offered} · 고름 {r.Chosen} · 사용 {r.Used} · 효과 {r.Changed} · 출처 {r.SourceEvents})");
            }
            Assert.IsEmpty(t.Errors, string.Join("\n", t.Errors));
        }
    }
}
