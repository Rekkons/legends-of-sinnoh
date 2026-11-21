using System;
using System.Collections.Generic;
using Sinnoh.Fx;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// UB 찾기(결정 189 ② — 사용자 2026-11-21): 위 띠 균열 칩(<see cref="BoardUb.Gauge"/>)을 누르면 카메라의 보는 점이 판에 선 UB 피규어로 가고
    /// (<see cref="Follow"/> — 방향 · 줌 그대로, 끌면 쉰다) 그 발밑 바닥 고리(<see cref="BeastRings"/>)가 번쩍인다. 누를 때마다 다음 UB(피규어 표 순서, 네크로즈마 포함).
    /// 대기열 밖의 즉시 연출이라 시각은 흐름 시계(<see cref="NowMs"/> — 연출 「빠름」이면 두 배, 「끄기」면 곧바로 끝 모습: 카메라는 그 자리 · 번쩍임 없음).
    /// 누를 수 있는 때 = 카메라가 키를 받는 때(<see cref="CanTurn"/> — 보드에 있고 위에 창 · 메뉴 · 배틀 화면이 없다, 남의 턴에도) [가정 — 결정 189].
    /// 화면 대기열이 도는 동안(UB 턴 · 남의 걷기 …)에는 연출의 카메라를 빼앗지 않고 고리만 번쩍인다 [가정 — 결정 189].
    /// </summary>
    public sealed partial class GameFlow
    {
        /// <summary>찾기 한 번의 길이(ms) · 번쩍임 한 번(켜짐 · 꺼짐 각각) — 세 번 번쩍인다 [가정].</summary>
        public const int FindMs = 1500, FindFlashMs = 250;

        // SyncFigures가 모으는 지금 판에 선 UB(피규어 표 순서): 토큰의 포켓몬 표 색인 · 발밑 땅의 점
        readonly List<int> beastDefs = new List<int>();
        readonly List<Vector3> beastGrounds = new List<Vector3>();
        int findDef = -1;
        long findAt = -1;
        Vector3 findPoint;
        FxStep findStep;

        /// <summary>판에 선 UB(피규어 표 순서 — 포켓몬 표 색인) · 그 발밑 땅의 점(확인용).</summary>
        public IReadOnlyList<int> BeastsShown => beastDefs;
        public IReadOnlyList<Vector3> BeastGrounds => beastGrounds;

        /// <summary>마지막으로 찾은 UB(포켓몬 표 색인, 없으면 −1) · 그 땅의 점 · 찾기가 도는 중인가(확인용).</summary>
        public int Found => findDef;
        public Vector3 FoundPoint => findPoint;
        public bool Finding => findAt >= 0;

        /// <summary>균열 칩을 눌러 UB를 찾을 수 있나: 판에 선 UB가 있고 카메라가 키를 받는 때.</summary>
        public bool CanFindBeast => CanTurn && beastDefs.Count > 0;

        /// <summary>균열 칩을 눌렀다: 다음 UB로 카메라를 옮기고 그 고리를 번쩍인다. 받았으면 true.</summary>
        public bool FindBeast()
        {
            if (!CanFindBeast) return false;
            int n = (beastDefs.IndexOf(findDef) + 1) % beastDefs.Count;
            findDef = beastDefs[n];
            findPoint = beastGrounds[n];
            findAt = NowMs;
            findStep = new FxStep { Row = -1, Ms = FindMs };
            FindTick();
            return true;
        }

        // 찾기 안 시각(−1 = 찾는 중 아님)
        int FindElapsed()
        {
            if (findAt < 0) return -1;
            if (FxSpeed == FxSpeed.Off) return FindMs;
            return (int)Math.Min(FindMs, (NowMs - findAt) * (FxSpeed == FxSpeed.Fast ? 2 : 1));
        }

        /// <summary>그 UB의 고리가 지금 번쩍이는 쪽인가.</summary>
        bool FindLit(int def)
        {
            int ms = FindElapsed();
            return def == findDef && ms >= 0 && ms < FindMs && (ms / FindFlashMs) % 2 == 0;
        }

        // 프레임마다(Tick): 대기열이 비었으면 카메라가 따라가고, 고리의 번쩍임을 다시 그린다. 끝나면 고리를 제 색으로
        void FindTick()
        {
            int ms = FindElapsed();
            if (ms < 0) return;
            if (Fx == null || Fx.Sequencer.IsEmpty) Follow(findStep, findPoint, ms);
            if (ms >= FindMs) findAt = -1;
            SyncFigures();
        }
    }
}
