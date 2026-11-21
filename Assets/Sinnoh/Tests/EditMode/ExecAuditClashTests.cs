using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Rules;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M9 실행 감사 ④ 트레이너 능력 충돌 장면(<see cref="ExecAuditClashes"/>): 쌍 × 변형(둘 다 · A만 · B만)마다 시험 하나. 기대와 다른 것은 <see cref="ExecAuditClashes.Known"/>에 적어 두고
    /// 아직 재현되는지 본다(게임 동작은 고치지 않는다). 분류표가 데이터의 카드 능력을 모두 덮는지 · 후보 쌍과 덮은 범위도 여기서.
    /// </summary>
    public class ExecAuditClashTests
    {
        Ctx ctx;

        [OneTimeSetUp]
        public void LoadOnce() => ctx = TestData.LoadCtx();

        static IEnumerable<TestCaseData> Cases()
        {
            foreach (var row in ExecAuditClashes.Rows())
                foreach (ExecAuditClashes.Use u in System.Enum.GetValues(typeof(ExecAuditClashes.Use)))
                    yield return new TestCaseData(row.Id, u);
        }

        static ExecAuditClashes.Row RowOf(string id) => new List<ExecAuditClashes.Row>(ExecAuditClashes.Rows()).Find(r => r.Id == id);

        [TestCaseSource(nameof(Cases))]
        public void M9_실행감사_충돌장면(string id, ExecAuditClashes.Use use)
        {
            var row = RowOf(id);
            var o = row.Run(ctx, use);
            string key = $"{id}_{use}";
            foreach (var line in o.Seen) TestContext.WriteLine($"[{key}] {row.A} × {row.B}: {line}");
            foreach (var line in o.Miss) TestContext.WriteLine($"[{key}] 기대와 다름: {line}");
            if (ExecAuditClashes.Known.TryGetValue(key, out var why))
                Assert.IsNotEmpty(o.Miss, $"찾은 것 {key}({why})이 더는 재현되지 않는다 — 고쳤으면 Known에서 지운다");
            else
                Assert.IsEmpty(o.Miss, $"{row.A} × {row.B} {use} — 기대: {row.Expect} · 근거 {row.Rule}\n실제: {string.Join(" / ", o.Seen)}\n다른 곳: {string.Join(" / ", o.Miss)}");
        }

        static IEnumerable<ExecAuditClashes.Spot> PairSpots() => new[]
        {
            ExecAuditClashes.Spot.Order, ExecAuditClashes.Spot.BeforeRoll, ExecAuditClashes.Spot.AfterRoll, ExecAuditClashes.Spot.PreDamage, ExecAuditClashes.Spot.AfterDamage,
            ExecAuditClashes.Spot.BattleEnd, ExecAuditClashes.Spot.MapOther,
        };

        /// <summary>배틀 창의 후보 쌍 전부를 생성 장면으로(<see cref="ExecAuditClashes.RunPairs"/>): 서로 영향 없음 · 피해 = 각자의 합(상한 · 문턱은 순서대로) · 순서 값 = 각자의 합. 표에 없는 차이가 나면 실패.</summary>
        [TestCaseSource(nameof(PairSpots))]
        public void M9_실행감사_충돌쌍_생성장면(ExecAuditClashes.Spot spot)
        {
            var list = ExecAuditClashes.RunPairs(ctx, spot);
            foreach (var r in list) TestContext.WriteLine(r);
            TestContext.WriteLine(ExecAuditClashes.Summary(list));
            var bad = list.FindAll(r => r.Verdict == ExecAuditClashes.Verdict.Differs);
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        [Test]
        public void M9_실행감사_충돌후보_분류표가데이터의카드능력을모두덮고_장면은후보쌍이다()
        {
            var missing = new List<string>();
            var sources = ExecAuditClashes.Sources(ctx, missing);
            Assert.IsEmpty(missing, string.Join("\n", missing));
            int abilities = 0;
            foreach (var card in ctx.Cards.TrainerCards) abilities += card.Abilities.Count;
            Assert.AreEqual(102, abilities, "카드 능력 수(51장 × 2)");
            string report = ExecAuditClashes.Coverage(ctx, out int candidates, out int covered);
            foreach (var line in report.Split('\n')) TestContext.WriteLine(line);
            TestContext.WriteLine($"후보 쌍 {candidates} · 장면이 덮은 후보 쌍 {covered}");
            Assert.Greater(candidates, 0);
            Assert.Greater(covered, 10, "대표 쌍의 장면");
            var ids = new HashSet<string>();
            foreach (var row in ExecAuditClashes.Rows())
            {
                Assert.IsTrue(ids.Add(row.Id), "장면 id가 겹친다 " + row.Id);
                Assert.IsTrue(sources.Exists(x => x.Key == row.A) && sources.Exists(x => x.Key == row.B), $"{row.Id}: 출처 키가 분류표에 있다");
            }
            foreach (var key in ExecAuditClashes.Known.Keys) Assert.IsNotNull(RowOf(key.Split('_')[0]), "Known의 장면 " + key);
        }

        [Test, Explicit("손으로 — 결과 표를 Logs/Review/m9/exec-audit/clashes.tsv로")]
        public void M9_실행감사_충돌장면_표()
        {
            var sb = new System.Text.StringBuilder("id\twindow\ta\tb\tuse\texpect\trule\tactual\tmiss\tknown\n");
            foreach (var row in ExecAuditClashes.Rows())
                foreach (ExecAuditClashes.Use u in System.Enum.GetValues(typeof(ExecAuditClashes.Use)))
                {
                    var o = row.Run(ctx, u);
                    ExecAuditClashes.Known.TryGetValue($"{row.Id}_{u}", out var known);
                    sb.Append($"{row.Id}\t{row.Spot}\t{row.A}\t{row.B}\t{u}\t{row.Expect}\t{row.Rule}\t{string.Join(" / ", o.Seen)}\t{string.Join(" / ", o.Miss)}\t{known}\n");
                }
            sb.Append("# ").Append(ExecAuditClashes.Coverage(ctx, out _, out _).Replace("\n", "\n# ")).Append('\n');
            string dir = Path.Combine(TestData.RepoRoot(), "Logs", "Review", "m9", "exec-audit");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "clashes.tsv"), sb.ToString(), new System.Text.UTF8Encoding(false));
            // 후보 쌍 전부(장면 · 생성 장면 · 까닭)
            var pairs = new System.Text.StringBuilder("window\tpair\tverdict\tnote\n");
            foreach (ExecAuditClashes.Spot spot in System.Enum.GetValues(typeof(ExecAuditClashes.Spot)))
            {
                if (spot == ExecAuditClashes.Spot.Own) continue;
                var list = ExecAuditClashes.RunPairs(ctx, spot);
                foreach (var r in list) pairs.Append(r).Append('\n');
                pairs.Append($"# {spot}: {ExecAuditClashes.Summary(list)}\n");
            }
            File.WriteAllText(Path.Combine(dir, "clash-pairs.tsv"), pairs.ToString(), new System.Text.UTF8Encoding(false));
        }
    }
}
