using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Sinnoh.Client;
using Sinnoh.Fx;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Sinnoh.Tests.PlayMode
{
    /// <summary>
    /// 사용자 결정 167 · 168(2026-11-21): 공중날기 앞의 비전기술 컷인(16행 단계의 앞 — 끄기에서 건너뜀 · 스페이스 바 = 착지 · 남 좌석이 날 때도 그 좌석의 주인공으로)과
    /// 걷기 전 확인에서 고른 길의 칸마다 파란 육각 테두리(경유 칸 · 고른 칸 뺀 사이 칸 — 길이 바뀌면 따라 바뀌고 걷기를 시작하면 걷힌다).
    /// 판은 첫 턴부터 공중날기가 열리는 확인 판(<see cref="MouseProbe.StartFlyGame"/>와 같은 손봄 — 고향이 아닌 마을 · 빨강 규토리).
    /// </summary>
    public sealed class FlyCutinFlowTests
    {
        const int W = 1920, H = 1080;
        const ulong Seed = 20261112, AiSeed = 7;
        ScreenBoot boot;
        GameFlow flow;
        UiStage stage;
        RenderTexture rt;
        FxSpeed speedWas;
        readonly List<string> errors = new List<string>();

        void Grab(string msg, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + msg);
        }

        [TearDown]
        public void Shut()
        {
            Application.logMessageReceived -= Grab;
            if (flow != null) flow.FxSpeed = speedWas;
            var b = Object.FindAnyObjectByType<ScreenBoot>();
            if (b != null && b.Root != null) b.Root.SetTarget(null);
            if (rt != null)
            {
                rt.Release();
                rt = null;
            }
        }

        IEnumerator Boot()
        {
            errors.Clear();
            Application.logMessageReceived += Grab;
            yield return SceneManager.LoadSceneAsync("Board", LoadSceneMode.Single);
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                boot = Object.FindAnyObjectByType<ScreenBoot>();
                if (boot != null && boot.Root != null && boot.Root.Stage.Root != null) break;
            }
            Assert.That(boot, Is.Not.Null, "씬에 ScreenBoot 없음");
            Assert.That(boot.Root, Is.Not.Null, "화면 뿌리 없음(Gen 글꼴·창틀 — rebuild-all)");
            flow = boot.Flow;
            stage = boot.Root.Stage;
            speedWas = flow.FxSpeed;
            boot.PointerFromMouse = false;
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            boot.Root.SetTarget(rt);
            for (int i = 0; i < 4; i++) yield return null;
        }

        IEnumerator Until(Func<bool> until, string what)
        {
            for (int i = 0; i < 600 && !until(); i++)
            {
                flow.Skip(100);
                yield return null;
            }
            Assert.That(until(), Is.True, what + " — 화면 " + (stage.Current != null ? stage.Current.Name : "보드"));
        }

        // seats명 판: flier 좌석을 고향이 아닌 마을에 세우고 빨강 규토리를 쥐여 준다(첫 턴부터 공중날기)
        void StartFly(int seats, int flier)
        {
            var ctx = flow.Ctx;
            flow.StartSeats(new SoloSettings(), seats, Seed, AiSeed, s =>
            {
                var st = s.Seats[flier];
                var fixtures = ctx.Fixtures.Fixtures;
                bool Home(int f) { foreach (var seat in s.Seats) if (seat.HomeVillage == f) return true; return false; }   // 고향 마을은 조무래기가 막는다
                for (int f = 0; f < fixtures.Count; f++)
                    if (fixtures[f].Kind == FixtureKind.Village && !Home(f) && !ctx.Board[fixtures[f].Cell].DistortionWorld) { st.Cell = fixtures[f].Cell; break; }
                st.Apricorns[(int)ApricornColor.Red] = Math.Max(1, st.Apricorns[(int)ApricornColor.Red]);
            });
        }

        // 내 [마을] → 첫 갈 수 있는 목적지 → [출발]
        IEnumerator FlyMine()
        {
            yield return Until(() => flow.InputOpen && flow.Hud != null && flow.Hud.Shows(BoardHud.Village), "옮겨 둔 마을의 [마을]");
            Assert.That(flow.Hud.Press(BoardHud.Village), Is.True);
            yield return null;
            var village = (VillageScreen)stage.Current;
            Assert.That(village.CanFly, Is.True, village.FlyWhy);
            int dest = -1;
            foreach (int f in village.Dests) if (village.CanGo(f)) { dest = f; break; }
            Assert.That(village.PickDest(dest), Is.True);
            Assert.That(village.PressFly(), Is.True, "[출발]");
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator M10_결정167_공중날기컷인은끄기에서건너뛰고_스페이스바는내공중날기를착지로보낸다()
        {
            yield return Boot();
            StartFly(1, 0);
            for (int i = 0; i < 6; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            flow.FxSpeed = FxSpeed.Off;
            yield return FlyMine();
            // 연출 「끄기」: 남은 대기열을 한 번에 보낸다 — 컷인 층은 한 프레임도 서지 않고 말은 바로 도착 마을에
            int to = flow.Client.View.Seats[0].Cell;
            for (int i = 0; i < 4; i++)
            {
                yield return null;
                Assert.That(flow.FlyFx == null || !flow.FlyFx.Shown, Is.True, i + "프레임: 끄기인데 컷인이 섰다");
            }
            yield return Until(() => flow.InputOpen, "끄기에서 입력이 열린다");
            Assert.That(flow.FlyFx == null || !flow.FlyFx.Shown, Is.True);
            Assert.That(Object.FindAnyObjectByType<BoardBoot>().Pieces.Of(0).Spot.Cell, Is.EqualTo(to), "말이 도착 마을에");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator M10_결정167_스페이스바는컷인중에도내공중날기를착지로보낸다()
        {
            yield return Boot();
            StartFly(1, 0);
            for (int i = 0; i < 6; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            flow.FxSpeed = FxSpeed.Normal;
            yield return FlyMine();
            stage.Tweens.Advance(flow.MsUntil(st => st.Row == FlyBeats.Row) + FlyCutinBeats.InMs);
            yield return null;
            var seq = flow.Fx.Sequencer;
            var step = FlyBeats.StepOf(seq.Current);
            Assert.That(step, Is.Not.Null, "공중날기 단계가 재생 중");
            Assert.That(flow.FlyFx.Shown && flow.FlyFx.Pose.Riders, Is.True, "컷인 가운데");
            Assert.That(flow.CanSkip, Is.True, "내 공중날기 = 건너뛰기 안내");
            Assert.That(flow.SkipKey(), Is.True, "스페이스 바");
            Assert.That(seq.LocalMs, Is.EqualTo(step.EndMs - SkipBeats.LandMs), "착지로 갔다");
            yield return null;
            Assert.That(flow.FlyFx.Shown, Is.False, "컷인이 걷혔다");
            Assert.That(flow.CanSkip, Is.False, "착지 뒤에는 건너뛰지 않는다");
            yield return Until(() => flow.InputOpen, "내려앉으면 입력이 열린다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator M10_결정167_남좌석이날때도컷인에그좌석주인공이타고_스페이스바는받지않는다()
        {
            yield return Boot();
            StartFly(2, 1);
            for (int i = 0; i < 6; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            yield return Until(() => flow.InputOpen && flow.Hud != null, "보드 입력");
            for (int k = 0; k < 4 && flow.Client.View.ActiveSeat != 1; k++)
            {
                Assert.That(flow.Hud.Press(BoardHud.EndTurn), Is.True, "내 [턴 종료]");
                yield return Until(() => flow.Client.View.ActiveSeat == 1 && flow.Fx.Sequencer.IsEmpty, "좌석 2의 턴");
            }
            Assert.That(flow.AnswerSeat(1, a => a.Kind == AnswerKind.Fly), Is.True, "좌석 2의 공중날기");
            stage.Tweens.Advance(flow.MsUntil(st => st.Row == FlyBeats.Row) + FlyCutinBeats.InMs);
            yield return null;
            Assert.That(FlyBeats.StepOf(flow.Fx.Sequencer.Current), Is.Not.Null);
            Assert.That(flow.FlyFx.Shown && flow.FlyFx.Pose.Riders, Is.True, "남의 공중날기도 컷인이 보인다(결정 120)");
            Assert.That(flow.FlyFx.RiderShown, Is.EqualTo(flow.Protagonist(1)), "탄 것은 날아가는 좌석의 주인공");
            Assert.That(flow.CanSkip, Is.False, "남의 공중날기는 스페이스 바로 건너뛰지 않는다");
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator M10_결정168_고른길의사이칸마다파란육각테두리가서고길이바뀌면따라가고걸으면걷힌다()
        {
            yield return Boot();
            StartFly(1, 0);
            for (int i = 0; i < 6; i++) yield return null;
            flow.Skip(flow.OpeningMs);
            yield return Until(() => flow.InputOpen && flow.Hud != null && flow.Hud.Reach.Count > 0, "걷기 입력");
            var hud = flow.Hud;
            var marks = flow.BoardMarks;
            // 사이 칸이 있고 그 길 밖의 경유 칸을 거쳐도 이동력 안인 고른 칸(길이 긴 것부터)
            var targets = new List<int>(hud.Reach);
            var lengths = new Dictionary<int, int>();
            foreach (int c in targets) { Assert.That(hud.PickCell(c), Is.True); lengths[c] = hud.PickedPath.Count; }
            targets.Sort((x, y) => lengths[y].CompareTo(lengths[x]));
            int far = -1, viaCell = -1;
            List<int> path = null;
            foreach (int t in targets)
            {
                if (lengths[t] < 3) break;
                Assert.That(hud.PickCell(t), Is.True);
                var p = hud.PickedPath;
                foreach (int c in hud.Reach)
                {
                    if (c == t || p.Contains(c) || !hud.AddVia(c)) continue;
                    far = t; viaCell = c; path = p;
                    break;
                }
                if (far >= 0) break;
            }
            Assert.That(far, Is.GreaterThanOrEqualTo(0), "사이 칸이 있는 길 + 길 밖 경유 칸");
            // 경유 칸을 더한 길: 경유 칸은 흰 테두리(파랑 아님), 나머지 사이 칸은 파랑
            var via = hud.PickedPath;
            Assert.That(marks.Held, Is.True);
            Assert.That(marks.Vias, Is.EqualTo(new[] { viaCell }));
            Assert.That(marks.Trail, Has.No.Member(viaCell), "경유 칸은 흰 테두리 그대로");
            Assert.That(marks.Trail.Count, Is.EqualTo(via.Count - 3), "경유 칸이 있는 길: 출발 · 경유 · 고른 칸 뺀 칸 수");
            for (int i = 1; i < via.Count - 1; i++) if (via[i] != viaCell) Assert.That(marks.Trail, Has.Member(via[i]));
            // 경유 칸을 빼면 처음 길로 따라 바뀐다
            Assert.That(hud.Back(), Is.True, "경유 칸 빼기");
            Assert.That(hud.PickedPath, Is.EqualTo(path));
            Assert.That(marks.Trail.Count, Is.EqualTo(path.Count - 2), "길 칸 표시 수 = 출발 · 고른 칸 뺀 길 칸 수");
            for (int i = 1; i < path.Count - 1; i++) Assert.That(marks.Trail, Has.Member(path[i]), "길 칸 " + path[i]);
            Assert.That(marks.Trail, Has.No.Member(path[0]).And.No.Member(far), "출발 칸 · 고른 칸(금색)은 파랑이 아니다");
            // 풀면 걷히고, 마우스를 올린 길(고르기 전)은 칠만 — 파랑은 고른 길에만
            Assert.That(hud.Unpick(), Is.True);
            Assert.That(marks.Trail.Count, Is.EqualTo(0), "풀면 걷힌다");
            Assert.That(hud.PickCell(far), Is.True);
            Assert.That(marks.Trail.Count, Is.EqualTo(path.Count - 2));
            // 걷기를 시작하면 이동 표시째 걷힌다
            Assert.That(hud.ClickCell(far), Is.True, "고른 칸을 다시 누르면 걷는다");
            yield return null;
            Assert.That(marks.Trail.Count, Is.EqualTo(0));
            Assert.That(marks.Visible, Is.False);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }
    }
}
