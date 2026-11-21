using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Sinnoh.Rules.Tutorial;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// M6.5 「클릭 경로·앵커 표」(07 §8 완료 조건 3·4): 사람 클릭 경로 — 본편(0장 → 7장 → preview2 → preview3, 목차의 장 고르기가 연 판) · 확장 장 견본 E1,
    /// 앵커 표 — 모든 각본(main · preview2 · preview3 · e01~e15)의 단계마다 앵커 첫 부분이 화면에 있다(빠짐 0). 틀은 <see cref="TutorialClickPath"/>.
    /// 확장 장 E2~E15의 클릭 경로 시험은 다음 묶음이 같은 틀로 더한다.
    /// </summary>
    public sealed class TutorialClickPathTests
    {
        // 화면에 뜨는 단계(막 앞 판 각본은 손보기 전 판 차리기 demo를 빨리감기로 지난다 — 07 T62)
        static List<string> Visible(params string[] ids)
        {
            var all = new List<string>();
            foreach (string id in ids)
            {
                string json = File.ReadAllText(Path.Combine(Application.dataPath, "Sinnoh/Data/Json/tutorial/" + id + ".json"));
                var script = Sinnoh.Data.TutorialLoader.Load(json, ScreenCtx(), "tutorial/" + id + ".json");
                int skip = script.Start.After != null ? 2 : 0;
                for (int i = 0; i < script.Steps.Count; i++)
                {
                    if (i >= skip) all.Add(id + "." + script.Steps[i].Id);
                    Assert.That(script.Steps[i].Text == null, Is.EqualTo(i < skip), $"{id}.{script.Steps[i].Id}: 빨리감기로 지나는 판 차리기만 말없는 단계(07 T160)");
                }
            }
            return all;
        }

        static Sinnoh.Rules.Ctx ScreenCtx() => Object.FindAnyObjectByType<Sinnoh.Client.ScreenBoot>().Flow.Ctx;

        [UnityTest]
        public IEnumerator M65_본편_클릭경로() => TutorialClickPath.Run("main0", p =>
        {
            CollectionAssert.AreEqual(Visible("main", "preview2", "preview3"), p.Steps, "본편 각본 셋의 단계를 차례로 모두 눌렀다");
            Assert.That(p.Menu.Record.Seen("main7"), Is.True, "끝까지 본 마지막 장은 완료");
            Assert.That(p.Drags, Is.GreaterThanOrEqualTo(1), "S505 비버니를 끌어 맨 앞에 놓았다(결정 107 ①)");
            Assert.That(p.NextHeld, Does.Contain("main.S506"), "S506 정비 창이 열린 채로는 [다음]이 없다(결정 107 ②)");
        });

        [UnityTest]
        public IEnumerator M65_E1_클릭경로() => TutorialClickPath.Run("e01", p =>
        {
            CollectionAssert.AreEqual(Visible("e01"), p.Steps, "E1 단계를 차례로 모두 눌렀다");
            Assert.That(p.Menu.Record.Seen("e01"), Is.True);
        });

        [UnityTest]
        public IEnumerator M65_앵커표_각본앵커가화면에모두있다()
        {
            var path = new TutorialClickPath();
            var errors = new List<string>();
            void Grab(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
            }

            Application.logMessageReceived += Grab;
            try
            {
                yield return path.Boot();
                var lines = new List<string>();
                var misses = new List<string>();
                var counts = new List<string>();
                var chapters = new List<string> { "main0" };
                foreach (var ch in path.Menu.Chapters)
                    if (!ch.IsMain && ch.Ready) chapters.Add(ch.Id);
                Assert.That(chapters.Count, Is.EqualTo(16), "본편 + 확장 장 15");
                int total = 0;
                foreach (string ch in chapters)
                {
                    int n = 0, before = misses.Count;
                    yield return path.Survey(ch, lines, misses, c => n = c);
                    total += n;
                    counts.Add($"{ch} {n}(빠짐 {misses.Count - before})");
                }
                Debug.Log($"[앵커 표] 합계: 앵커 {total} · 빠짐 {misses.Count} — " + string.Join(" · ", counts));
                Assert.That(misses, Is.Empty, "앵커 빠짐:\n" + string.Join("\n", misses));
                Assert.That(errors, Is.Empty, "에러 로그:\n" + string.Join("\n", errors));
            }
            finally
            {
                Application.logMessageReceived -= Grab;
                path.Release();
            }
        }
    }
}
