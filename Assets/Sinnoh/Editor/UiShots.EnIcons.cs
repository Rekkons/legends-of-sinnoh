using System;
using System.Collections.Generic;
using Sinnoh.Client;
using Sinnoh.Rules;
using UnityEditor;
using UnityEngine.UIElements;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 영문 아이콘(결정 115 — 영문판 Pt의 타입 · 상태 그림)의 확인 장면: 언어 갈고리 <see cref="IconArt.Lang"/>에 영어를 넣고 있던 장면을 그대로 세운 뒤,
    /// 화면의 타입 · 상태 칩이 모두 영문 그림(<c>Stage/Icons/en/</c>)인지 단정한다. 글은 한국어 그대로다(이 묶음은 그림만 — 화면 언어는 M9 언어 ②가 잇는다).
    /// 갈고리는 <see cref="Shoot(Action{UiShot}, bool, UiGrade, List{string}, int, int, string, List{string})"/>의 끝이 되돌린다. 그림은 <c>Logs/Review/m9/en-icons/</c>.
    /// </summary>
    public static partial class UiShots
    {
        public static readonly KeyValuePair<string, Action<UiShot>>[] EnIconScreens =
        {
            // 도감의 타입 거르기 줄(타입 없음을 뺀 18) + 목록 · 큰 그림의 타입 칩
            Screen("en-icons-dex", s => EnIcons(s, x => DexOpen(x, d => { d.PickDeck(DexDeck.Strong); d.PickType(PokemonType.Fire); }), 18, 0)),
            // 배틀 화면: 기둥 · 기술 줄의 타입 칩 + 상태 칩(기둥) + DS 체력 상자의 상태 배지
            Battle("battle-en-icons", s => EnIcons(s, x => LeftBattle(x, 4, false), 1, 1)),
        };

        static void EnIcons(UiShot s, Action<UiShot> build, int types, int statuses)
        {
            IconArt.Lang = IconArt.English;
            string was = Lang;
            Lang = IconArt.English;   // 무대가 영어 표로 시작한다 — UiStage가 언어를 그림 갈고리에 넣는다(결정 115)
            try { build(s); } finally { Lang = was; }
            s.Panel.Tick(3);
            int type = 0, status = 0;
            var seen = new HashSet<string>();
            s.Panel.Root.Query<VisualElement>().ForEach(e =>
            {
                var tex = e.resolvedStyle.backgroundImage.texture;
                if (tex == null) return;
                string path = AssetDatabase.GetAssetPath(tex);
                int at = path.IndexOf("/Gen/" + IconArt.Folder, StringComparison.Ordinal);
                if (at < 0) return;
                string rest = path.Substring(at + ("/Gen/" + IconArt.Folder).Length);
                if (rest.StartsWith("category_", StringComparison.Ordinal)) return;   // 글자 없는 그림은 한 벌
                if (!rest.StartsWith("en/", StringComparison.Ordinal)) throw new InvalidOperationException("영어 값인데 한국어 그림이다: " + path + "(" + e.name + ")");
                if (!seen.Add(rest)) return;
                if (rest.StartsWith("en/type_", StringComparison.Ordinal)) type++; else status++;
            });
            if (type < types || status < statuses) throw new InvalidOperationException($"영문 그림 칩이 모자란다: 타입 {type}/{types} · 상태 {status}/{statuses}");
        }
    }
}
