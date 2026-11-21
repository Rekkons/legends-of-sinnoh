using System;
using System.Collections.Generic;
using Sinnoh.Client;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 보드 돌리기 단추(결정 164 — 아래 판 오른쪽 위 ↺ Q · E ↻)와 배틀 독 단축키. 단추가 보이는 판(혼자 · 6인 · UB 6인 · 룰 설명서 옆 판) · 누른 뒤 · 숨는 때(게임 메뉴 · 배틀 화면).
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] RotateScreens =
        {
            Screen("rotate-board", s => { ToBoard(s, BoardReadyMs); RotateClear(s); }),                    // 혼자 판: 아래 판 오른쪽 위
            Screen("rotate-tip", s => { ToBoard(s, BoardReadyMs); RotateClear(s); RotateTip(s); }),         // ↺ 마우스 오버 「보드 돌리기 (Q)」
            Screen("rotate-6", s => { Seats(s, 6); RotateClear(s); }),                                      // 6인: 붐비는 위 띠 · 자원 줄
            Screen("rotate-ub6", s => { Gauge(s, UbGame(s, null, 6), false); RotateClear(s); }),           // UB 6인: 균열 칩 판(위 띠 아래 오른쪽)
            Screen("rotate-book", s => { BookSideFlow(s); RotateClear(s); }),                               // 룰 설명서 옆 판(오른쪽 기둥 — 아래 판 위 158)과 겹치지 않는다
            Screen("rotate-turned", s => RotateTurned(s)),                                                  // ↺ 한 번 = Q 한 번(방향 5), ↻ 두 번 = E 두 번(방향 1)
            Screen("rotate-menu", s => { var flow = ToBoard(s, BoardReadyMs); flow.OpenMenu(); RotateHidden(s, "게임 메뉴"); }),   // 창이 덮으면 숨는다(Q · E도 안 받는다)
            Battle("battle-rotate-keys", s => BattleKeys(s)),                                               // 배틀 화면: 단추 숨김 · 독의 단축키 글자 = 누르는 줄
        };

        // 돌리기 단추가 보이고 화면 안에 있고, 아래 판 · 자원 줄 · 기록 판 · 위 띠 · UB 칩 판 · 룰 설명서 옆 판 · 채팅과 겹치지 않고, 단추 둘이 묶음 안 · 글이 잘리지 않는다
        static void RotateClear(UiShot s)
        {
            var flow = s.Made;
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var root = flow.Stage.Root;
            var box = root.Q(BoardHud.Rotate);
            if (box == null || !flow.Hud.RotateShown || box.resolvedStyle.display == DisplayStyle.None || box.worldBound.width <= 0f) throw new InvalidOperationException("돌리기 단추가 보이지 않는다(CanTurn " + flow.CanTurn + ")");
            var b = box.worldBound;
            var screen = s.Panel.Root.worldBound;
            if (b.xMin < screen.xMin - 0.5f || b.yMin < screen.yMin - 0.5f || b.xMax > screen.xMax + 0.5f || b.yMax > screen.yMax + 0.5f) throw new InvalidOperationException("돌리기 단추가 화면 밖으로 나간다(" + b + ")");
            foreach (string other in new[] { "board-hud-dock", BoardHud.Res, "board-hud-log", "board-hud-seats", "board-hud-status", "board-hud-ub-row", "rulebook-side", "chat" }) Apart(root, BoardHud.Rotate, other);
            foreach (string name in new[] { BoardHud.RotateLeft, BoardHud.RotateRight })
            {
                var button = root.Q(name) ?? throw new InvalidOperationException(name + "이 없다");
                var e = button.worldBound;
                if (e.width <= 0f || e.xMin < b.xMin - 0.5f || e.xMax > b.xMax + 0.5f || e.yMin < b.yMin - 0.5f || e.yMax > b.yMax + 0.5f) throw new InvalidOperationException(name + "이 묶음 밖으로 나간다(" + e + " · 묶음 " + b + ")");
            }
            var bad = WorkUi.Overflows(box);
            if (bad.Count > 0) throw new InvalidOperationException("돌리기 단추 글 넘침: " + string.Join(" / ", bad));
            UnityEngine.Debug.Log($"[UiShots] rotate {s.Grade.Height}: 묶음 {b} · 아래 판 {root.Q("board-hud-dock").worldBound}");
        }

        static void RotateTip(UiShot s)
        {
            var stage = s.Made.Stage;
            var left = stage.Root.Q(BoardHud.RotateLeft);
            stage.Tips.Show(left);
            s.Panel.Tick(3);
            if (stage.Tips.Target != left) throw new InvalidOperationException("↺ 툴팁이 뜨지 않았다");
        }

        static void RotateHidden(UiShot s, string why)
        {
            var flow = s.Made;
            flow.Layout(s.Width, s.Height);
            s.Panel.Tick(3);
            var box = flow.Stage.Root.Q(BoardHud.Rotate);
            if (flow.CanTurn) throw new InvalidOperationException(why + ": 카메라가 Q · E를 받는다");
            if (box != null && box.resolvedStyle.display != DisplayStyle.None) throw new InvalidOperationException(why + ": 돌리기 단추가 보인다");
        }

        static readonly System.Reflection.MethodInfo ClickInvoke = typeof(Clickable).GetMethod("Invoke", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public, null, new[] { typeof(EventBase) }, null);

        // ↺ · ↻ 누르기(단추의 Clickable — 단추에 건 길 그대로)가 카메라 방향을 Q · E처럼 60°씩 돌린다
        static void RotateTurned(UiShot s)
        {
            var flow = ToBoard(s, BoardReadyMs);
            RotateClear(s);
            int was = s.Rig.Yaw;
            var root = flow.Stage.Root;
            void Click(string name)
            {
                var button = root.Q<Button>(name) ?? throw new InvalidOperationException(name + "이 없다");
                if (ClickInvoke == null) throw new InvalidOperationException("Clickable.Invoke(EventBase)를 찾지 못했다(Unity 판이 바뀌었다)");
                using (var e = ClickEvent.GetPooled())
                {
                    e.target = button;
                    ClickInvoke.Invoke(button.clickable, new object[] { e });   // 단추의 Clickable — 마우스를 뗄 때와 같은 동작(PlayMode TutorialClickPath와 같은 꼴)
                }
            }
            Click(BoardHud.RotateLeft);
            int left = s.Rig.Yaw;
            Click(BoardHud.RotateRight);
            Click(BoardHud.RotateRight);
            if (left != (was + 5) % 6 || s.Rig.Yaw != (was + 1) % 6) throw new InvalidOperationException("돌리기 단추: " + was + " → ↺ " + left + " → ↻↻ " + s.Rig.Yaw);
            RotateClear(s);
        }

        // 배틀 화면(1막 조무래기 — 트레이너 배틀이라 [가방]은 꺼짐): 돌리기 단추 · 카메라 키 꺼짐, 단축키 1 = 기술 줄 1 · Q · E = 꺼진 단추라 없음
        static void BattleKeys(UiShot s)
        {
            var flow = ToBattle(s);
            RotateHidden(s, "배틀 화면");
            var battle = (BattleScreen)flow.Stage.Current;
            var root = flow.Stage.Root;
            if (battle.KeyTarget('1') == null || battle.KeyTarget('1') != root.Q("battle-move-1")) throw new InvalidOperationException("단축키 1이 기술 줄 1이 아니다");
            if (battle.KeyTarget('Q') != null && !battle.BagOn) throw new InvalidOperationException("꺼진 [가방]을 Q가 누른다");
            if (battle.KeyTarget('E') != null && !battle.ForfeitOn) throw new InvalidOperationException("꺼진 [기권]을 E가 누른다");
            int yaw = s.Rig.Yaw;
            if (flow.Turn(1) || s.Rig.Yaw != yaw) throw new InvalidOperationException("배틀 화면 위에서 보드가 돌았다");
            UnityEngine.Debug.Log($"[UiShots] battle-rotate-keys {s.Grade.Height}: 1 = {battle.KeyTarget('1')?.name} · Q = {battle.KeyTarget('Q')?.name ?? "없음"} · E = {battle.KeyTarget('E')?.name ?? "없음"}");
        }
    }
}
