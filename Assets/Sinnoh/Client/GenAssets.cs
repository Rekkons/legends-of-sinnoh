using System;
#if !UNITY_EDITOR && !UNITY_WEBGL
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#endif

namespace Sinnoh.Client
{
    /// <summary>
    /// Gen/ 에셋을 키(`Assets/Sinnoh/Gen/` 아래 상대 경로, 예 `Board/hex_Standard.png`)로 읽는다(03 §0, d3-3 §4-2 [가정 M4-2a]).
    /// 추적하는 씬·프리팹은 Gen GUID를 갖지 않는다(precheck 「ref」). Gen 본체가 없으면 null.
    /// 에디터(PlayMode 시험·배치 캡처)는 AssetDatabase. Win64 플레이어는 `StreamingAssets/content/` AssetBundle(M4-2e, Sinnoh.Editor.BoardBuild가 만든다, 03 §5).
    /// WebGL은 같은 번들을 페이지 주소의 <c>StreamingAssets/content/&lt;이름&gt;.gz</c>에서 내려받아 읽는다(<see cref="WebContent"/> — M7 웹 손님, 03 §5): 첫 받기는 타이틀 전에, 피규어 · 곡은 그 뒤 뒤에서.
    /// </summary>
    public static class GenAssets
    {
        public const string Root = "Assets/Sinnoh/Gen/";

        /// <summary>플레이어 번들 폴더(`StreamingAssets/content/`)이자 그 폴더 manifest 번들 이름.</summary>
        public const string ContentFolder = "content";

        /// <summary>피규어 번들이 함께 쓰는 URP 셰이더 번들(03 §2-5 크기 재기와 같은 묶음).</summary>
        public const string FigureShadersBundle = "fig_shaders";

        /// <summary>
        /// 키 → 플레이어 번들 이름 [가정 M4-2e, d3-3 §4-2]: `Board/**` → board, `Trainers/trainers.json`·`Trainers/Front/**` → trainers,
        /// `Figures/figures.json` → figures, PC 층 겉모양(Galmuri11 글꼴·Pt 창틀 둘 — UiSkin의 키 셋) → ui, `Figures/&lt;폴더&gt;/**` → fig_&lt;폴더&gt;(피규어 하나 = 번들 하나),
        /// 셋업 화면이 읽는 것(M4-3): 포켓몬 정지 도트 아틀라스 `Sprites/Static/**` → sprites, 마을 카드 그림 `Cards/Village/**`와 체력·스피드 아이콘 `Cards/Generator/stats_icons/**` → cards.
        /// 배틀 화면의 DS 층(M4-3): 무대·체력 상자·창틀·타입·상태 그림 `Stage/**`(위 ui의 창틀 둘은 빼고)과 원작 자산 글꼴 `Fonts/pt_*` → stage.
        /// 주인공 도트 말·주인공 고르기(M4-3, 결정 52·60): 필드 도트 표와 시트 `Field/**` → trainers.
        /// 얼굴 상자(M4-3, 결정 54 ④·61 ③): 주인공 앞모습 8 `Trainers/Hero/**` → trainers.
        /// 조우·포획 화면(M4-3): 조우 등급 그림 `Cards/Generator/encounter_icons/**`와 볼·규토리 그림(`Cards/Tokens/item_icons/`의 `*_ball.png`·`apricorn_*.png` — <see cref="IsItemIcon"/>) → cards.
        /// 셋업의 트레이너 카드 뒷면(결정 70 ④ A8 ②): 세로 뒷면 부품 둘 <c>Cards/Trainer/back_*.png</c> → cards. 해금 연출(M6 — 04 48·49·50행)의 원작 카드 틀 <c>Cards/Trainer/</c> 바로 아래의 나머지 열셋(등급별 바탕·아래 띠·뒷면 · 자물쇠 둘 · 등급 아이콘 둘) → cards.
        /// 운명의 변화 카드(M5 「운명·응답 화면」): 앞면 넷 + 뒷면 <c>Cards/Fate/**</c> → cards. 사당 카드 그림 <c>Cards/Shrine/**</c>와 종막 문양 <c>Cards/Act/ending.png</c>(결정 80 L1·L12 나) → cards.
        /// 트레이너 카드 전용 아이템 아이콘 <c>Cards/Trainer/icons/**</c>와 왜곡 균열 카드 그림 <c>Cards/Breach/**</c>(M6 그림 에셋) → cards.
        /// 금색 ? 뒷면 <c>Cards/Tokens/item_tokens/hidden_spot_special_back.png</c>(결정 87 V8 나)와 울트라 온슬롯 카드 그림 <c>Cards/Onslaught/art.png</c>(결정 87 V24 나) → cards.
        /// 소리(M4-3 소리 뼈대, 결정 66 ④): 상황 곡 표·효과음 표와 그 클립 <c>Audio/**</c> → audio(번들에 넣는 것은 표가 가리키는 곡만 — <c>BoardBuild.Plan</c>). 울음 <c>Audio/Cry/**</c> → cries(M9 소리 ③, 나눠 받기).
        /// 튜토리얼 안내 마박사(M6.5, 결정 90 Q2 가): 소개 전신 도트 <see cref="RowanFull"/>와 목차 얼굴 필드 도트 <see cref="RowanFace"/> → tutorial.
        /// 설명서 그림(M9 「설명서 그림」, 결정 35): 원작 룰북에서 자른 예시 그림과 그 표 <c>Rulebook/**</c> → rulebook(웹 손님판은 나눠 받기 — <see cref="Split"/>).
        /// 포켓몬 카드(결정 206): 구운 카드 그림과 카드에 얹는 것 → <see cref="CardBundle"/>(PC만 — <see cref="WebSkips"/>).
        /// 화면이 아직 읽지 않는 키는 null(그 화면을 만들 때·M7에 더한다).
        /// 에디터 번들 만들기(BoardBuild)와 플레이어 읽기가 이 한 곳을 같이 쓴다.
        /// </summary>
        public static string BundleFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (key.StartsWith("Board/", StringComparison.Ordinal)) return "board";
            if (key == "Trainers/trainers.json" || key.StartsWith("Trainers/Front/", StringComparison.Ordinal) || key.StartsWith("Trainers/Back/official/", StringComparison.Ordinal)) return "trainers";
            if (key.StartsWith("Trainers/Hero/", StringComparison.Ordinal)) return "trainers";
            if (key.StartsWith("Field/", StringComparison.Ordinal)) return "trainers";
            if (key == UiSkin.FontKey || key == UiSkin.PanelFrameKey || key == UiSkin.TipFrameKey) return "ui";
            if (key.StartsWith("Stage/", StringComparison.Ordinal) || key.StartsWith("Fonts/pt_", StringComparison.Ordinal)) return "stage";
            if (key.StartsWith("Sprites/Static/", StringComparison.Ordinal)) return "sprites";
            if (IsCardArt(key)) return CardBundleFor(key);
            if (key.StartsWith("Cards/Village/", StringComparison.Ordinal) || key.StartsWith("Cards/Generator/stats_icons/", StringComparison.Ordinal)) return "cards";
            if (key.StartsWith("Cards/Generator/encounter_icons/", StringComparison.Ordinal) || IsItemIcon(key)) return "cards";
            if (key.StartsWith(TrainerFrame, StringComparison.Ordinal) && key.IndexOf('/', TrainerFrame.Length) < 0 && key.EndsWith(".png", StringComparison.Ordinal)) return "cards";   // 세로 뒷면 부품 둘 + 카드 틀(해금 연출)
            if (key.StartsWith(FateArt, StringComparison.Ordinal)) return "cards";
            if ((key.StartsWith(ShrineArt, StringComparison.Ordinal) && key.EndsWith(".png", StringComparison.Ordinal)) || key == EndingArt) return "cards";
            if ((key.StartsWith(TrainerItemIcons, StringComparison.Ordinal) || key.StartsWith(BreachArt, StringComparison.Ordinal)) && key.EndsWith(".png", StringComparison.Ordinal)) return "cards";
            if (key.StartsWith(GodIcons, StringComparison.Ordinal) && !key.EndsWith("arceus.png", StringComparison.Ordinal)) return "cards";
            if (IsSpotToken(key) || key == SpecialSpotBack || key == OnslaughtArt || key == StampSheet || key == StampSeal) return "cards";
            if (key.StartsWith(MusicFolder, StringComparison.Ordinal) && key != MusicTable) return MusicBundle(key);
            if (key.StartsWith(CryFolder, StringComparison.Ordinal)) return CryBundle;
            if (key.StartsWith("Audio/", StringComparison.Ordinal)) return "audio";
            if (key == RowanFull || key == RowanFace) return "tutorial";
            if (key.StartsWith(RulebookFolder, StringComparison.Ordinal)) return RulebookBundle;
            if (key == "Figures/figures.json") return "figures";
            if (key.StartsWith("Figures/", StringComparison.Ordinal))
            {
                int slash = key.IndexOf('/', "Figures/".Length);
                return slash > "Figures/".Length ? "fig_" + key.Substring("Figures/".Length, slash - "Figures/".Length).ToLowerInvariant() : null;
            }
            return null;
        }

        /// <summary>
        /// 포켓몬 카드 번들(결정 206 1단계 — <see cref="CardStack"/>): 구운 앞면 · 기술 띠 · 균열 · 전술 판 띠 · 숫자 글자(<see cref="CardFaces"/> · <see cref="CardMoves"/> · <see cref="CardStrips"/> · <see cref="CardDigits"/>),
        /// 카드에 얹는 상태 주사위 면 <c>Cards/Dice/status_*.png</c> · ♥ · ≫ 상자 <c>Cards/Generator/stats_bases/</c> · 생성기 타입 그림 <c>Cards/Generator/types/</c> · 지닌물건 토큰(<see cref="HeldTokens"/>).
        /// PC(Win64)만 싣는다 — 웹 손님판 빌드에는 넣지 않는다(<see cref="WebSkips"/>, 첫 받기 40 MB 문턱 · [가정] 웹은 도트 카드로 대신, 뒤에서 받기는 다음 단계).
        /// </summary>
        public const string CardBundle = "pokecards";
        public const string CardFaces = "Cards/Faces/", CardMoves = "Cards/Moves/", CardStrips = "Cards/Strips/", CardDigits = "Cards/Digits/";

        /// <summary>카드 지닌물건 동그라미에 얹는 토큰(지닌물건 아이템 <c>item.&lt;이름&gt;</c> — 유물 · 우두머리 토큰은 <see cref="SpotTokens"/>로 cards 번들).</summary>
        public static readonly string[] HeldTokens = { "blast_crystal", "choice_band", "eviolite", "heart_of_sinnoh", "lucky_egg", "scroll_of_nobility", "star_shield", "swiftness_sash" };

        /// <summary>
        /// 카드 그림의 번들(자동 업데이트는 파일 단위 — 카드 한 장을 고치면 그 묶음만 다시 받게 나눈다):
        /// 앞면 <c>Cards/Faces/&lt;언어&gt;/&lt;도감 번호&gt;_…</c> → <c>pokecards_&lt;언어&gt;_&lt;도감 번호 백 단위&gt;</c>(예 <c>pokecards_ko_400</c> = 400~499번),
        /// 기술 띠 · 균열 · 전술 판 띠 <c>Cards/Moves|Strips/&lt;언어&gt;/</c> → <c>pokecards_&lt;언어&gt;_moves</c>, 나머지(숫자 글자 · 문장 · 주사위 · 상자 · 타입 · 지닌물건) → <see cref="CardBundle"/>.
        /// </summary>
        static string CardBundleFor(string key)
        {
            string lang = LangOf(key, CardFaces);
            if (lang != null)
            {
                int i = CardFaces.Length + lang.Length + 1, dex = 0;
                while (i < key.Length && key[i] >= '0' && key[i] <= '9') dex = dex * 10 + (key[i++] - '0');
                return CardBundle + "_" + lang + "_" + (dex / 100 * 100).ToString("000", System.Globalization.CultureInfo.InvariantCulture);
            }
            lang = LangOf(key, CardMoves) ?? LangOf(key, CardStrips);
            return lang != null ? CardBundle + "_" + lang + "_moves" : CardBundle;
        }

        /// <summary><c>&lt;폴더&gt;&lt;언어&gt;/&lt;파일&gt;</c>의 언어(폴더 바로 아래 파일이면 null).</summary>
        static string LangOf(string key, string folder)
        {
            if (!key.StartsWith(folder, StringComparison.Ordinal)) return null;
            int slash = key.IndexOf('/', folder.Length);
            return slash > folder.Length ? key.Substring(folder.Length, slash - folder.Length) : null;
        }

        /// <summary>웹 손님판 빌드에서 빼는 번들(카드 그림 — <see cref="CardBundle"/>와 그 나눈 묶음 <c>pokecards_*</c>).</summary>
        public static bool WebSkips(string bundle) => bundle != null && (bundle == CardBundle || bundle.StartsWith(CardBundle + "_", StringComparison.Ordinal));

        static bool IsCardArt(string key)
        {
            if (!key.EndsWith(".png", StringComparison.Ordinal)) return false;
            if (key.StartsWith(CardFaces, StringComparison.Ordinal) || key.StartsWith(CardMoves, StringComparison.Ordinal) || key.StartsWith(CardStrips, StringComparison.Ordinal) || key.StartsWith(CardDigits, StringComparison.Ordinal)) return true;
            if (key.StartsWith("Cards/Dice/status_", StringComparison.Ordinal) || key.StartsWith("Cards/Generator/stats_bases/", StringComparison.Ordinal) || key.StartsWith("Cards/Generator/types/", StringComparison.Ordinal)) return true;
            return key.StartsWith(ItemTokens, StringComparison.Ordinal) && Array.IndexOf(HeldTokens, key.Substring(ItemTokens.Length, key.Length - ItemTokens.Length - 4)) >= 0;
        }

        const string MusicFolder = "Audio/Bgm/", MusicTable = "Audio/Bgm/bgm.json", CryFolder = "Audio/Cry/";

        /// <summary>울음 표 + 울음 322(M9 소리 ③ — <c>Audio/Cry/**</c>) = 번들 하나. 웹 손님판은 나눠 받는다(첫 받기 문턱 40 MB 밖 — 03 §5 「울음소리 나눠 받음」).</summary>
        public const string CryBundle = "cries";

        /// <summary>곡 하나 = 번들 하나(M7 웹 손님 나눠 받기, 03 §5 [가정 M7-63] — 반복 클립 + 앞부분 클립 <c>.intro</c>가 한 번들): <c>bgm_&lt;파일 이름(첫 점 앞, 소문자, 영숫자 밖은 _)&gt;</c>.</summary>
        public const string MusicBundlePrefix = "bgm_";

        static string MusicBundle(string key)
        {
            string name = key.Substring(MusicFolder.Length);
            int dot = name.IndexOf('.', name.LastIndexOf('/') + 1);   // 첫 점까지 — 앞부분 클립 <곡>.intro.wav도 그 곡의 번들(M9 소리 ②)
            if (dot > 0) name = name.Substring(0, dot);
            var sb = new System.Text.StringBuilder(MusicBundlePrefix);
            foreach (char c in name.ToLowerInvariant()) sb.Append(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' ? c : '_');
            return sb.ToString();
        }

        /// <summary>
        /// 웹 손님판이 나중에 받는 번들인가(03 §5 「나눠 받기」): 피규어 하나씩(<c>fig_*</c> · 셰이더 공용)과 곡 하나씩(<c>bgm_*</c>), 설명서 그림(<see cref="RulebookBundle"/> — M9) · 울음 묶음(<see cref="CryBundle"/>). 나머지(글꼴 · 창틀 · 보드 · 카드 · 무대 · 효과음 …)는 첫 받기 —
        /// 타이틀을 열기 전에 다 받는다(<see cref="Ready"/>).
        /// </summary>
        public static bool Split(string bundle) =>
            bundle != null && (bundle.StartsWith("fig_", StringComparison.Ordinal) || bundle.StartsWith(MusicBundlePrefix, StringComparison.Ordinal) || bundle == RulebookBundle || bundle == CryBundle);

        /// <summary>설명서 그림 폴더(M9 「설명서 그림」 — Tools/assets/rulebook.mjs가 원작 룰북 PDF에서 자른다): <c>Rulebook/&lt;절 키&gt;/&lt;이름&gt;[.&lt;언어&gt;].png</c>.</summary>
        public const string RulebookFolder = "Rulebook/";

        /// <summary>설명서 그림 표: 행 = { key(설명서 그림 키), ko, en(그 언어의 Gen 키 — 그림 속 글이 없으면 같은 키), w, h }.</summary>
        public const string RulebookTable = RulebookFolder + "figures.json";

        /// <summary>설명서 그림 번들(나눠 받기 — 첫 받기 40 MB 문턱 밖).</summary>
        public const string RulebookBundle = "rulebook";

        /// <summary>튜토리얼 안내 마박사 — Pt 새 게임 소개 장면의 전신 도트 64×123(원작 자산 demo/intro/intro.narc 19·20·23).</summary>
        public const string RowanFull = "Tutorial/rowan_full.png";

        /// <summary>튜토리얼 목차의 작은 마박사 얼굴 — Pt 필드 도트 prof_rowan의 남쪽 선 모습 32×32.</summary>
        public const string RowanFace = "Tutorial/rowan_face.png";

        public const string ItemIcons = "Cards/Tokens/item_icons/";

        /// <summary>신오의 신 아이콘(추종자 — 디아루가 · 펄기아 · 기라티나 <c>&lt;신&gt;.png</c>, M5 「사당·균열 화면」) → cards.</summary>
        public const string GodIcons = "Cards/Tokens/celestial_icons/";

        /// <summary>운명의 변화 카드 그림(앞면 넷 <c>&lt;카드 키 끝&gt;.png</c> + 뒷면 <c>back.png</c> — M5 「운명·응답 화면」) → cards.</summary>
        public const string FateArt = "Cards/Fate/";

        /// <summary>사당 카드의 그림 부분(전설 이벤트 <c>&lt;카드 키 끝&gt;.png</c> 여덟 + 사당의 축복이 같이 쓰는 <c>blessing.png</c> — 결정 80 L12 나) → cards.</summary>
        public const string ShrineArt = "Cards/Shrine/", ShrineBlessingArt = ShrineArt + "blessing.png";

        /// <summary>트레이너 카드 전용 아이템 아이콘(rulings G1 — <c>&lt;아이템 키 끝&gt;.png</c> 열하나, 지닌물건 아이콘과 같은 꼴 256² — M6 그림 에셋) → cards.</summary>
        public const string TrainerItemIcons = "Cards/Trainer/icons/";

        /// <summary>트레이너 카드 틀 그림 폴더(바로 아래 png — 셋업 뒷면 부품 · 해금 연출의 등급별 바탕·아래 띠·뒷면 · 자물쇠 · 등급 아이콘) → cards.</summary>
        public const string TrainerFrame = "Cards/Trainer/";

        /// <summary>왜곡 균열 카드의 그림 부분(<c>&lt;카드 키 끝&gt;.png</c> 여섯 — 글은 우리 글씨로, M6 그림 에셋) → cards.</summary>
        public const string BreachArt = "Cards/Breach/";

        /// <summary>울트라 온슬롯 카드의 그림 부분(원작 여섯 장의 그림이 같아 한 장 — 글은 우리 글씨로, 결정 87 V24 나) → cards.</summary>
        public const string OnslaughtArt = "Cards/Onslaught/art.png";

        /// <summary>여행 스탬프 시트 앞면(원작 그림 — 동그라미 일곱, 자리 표는 <c>StampScreen.Spots</c>)과 도장(밀랍 봉인 토큰) — M9 다듬기 P12, 결정 80 L18 나 → cards.</summary>
        public const string StampSheet = "Cards/Stamp/sheet.png", StampSeal = "Cards/Stamp/seal.png";

        /// <summary>종막 카드 뒷면의 금빛 문양(원작 종막 카드는 앞면이 글뿐 — 결정 80 L1 나) → cards.</summary>
        public const string EndingArt = "Cards/Act/ending.png";

        /// <summary>아이템 토큰 그림(원작 TTS 토큰 — <c>Cards/Tokens/item_tokens/&lt;이름&gt;.png</c>).</summary>
        public const string ItemTokens = "Cards/Tokens/item_tokens/";

        /// <summary>특별한 숨겨진 장소(금색 ?)의 뒷면 — 원작 TTS 금색 토큰(결정 87 V8 나, 보통 뒷면 <c>Board/Icons/hidden_spot_back.png</c>과 같은 틀) → cards.</summary>
        public const string SpecialSpotBack = ItemTokens + "hidden_spot_special_back.png";

        /// <summary>숨겨진 장소에서 나오는 유물 여덟 · 천상의 유물 셋 · 우두머리 토큰의 이름(아이템 키 <c>item.&lt;이름&gt;</c> — M5 「맵 결과·배틀 안 연출」 26~30행의 앞면 그림) → cards.</summary>
        public static readonly string[] SpotTokens =
        {
            "alpha_pokemon", "gracidea_flower", "icesteel_core", "magma_stone", "odd_keystone", "rockice_core", "seafarer_egg", "secret_key", "steelrock_core",
            "adamant_artifact", "griseous_artifact", "lustrous_artifact",
        };

        /// <summary>그 아이템 키(<c>item.&lt;이름&gt;</c>)의 토큰 그림 키. 유물·우두머리가 아니면 null.</summary>
        public static string SpotTokenKey(string itemKey)
        {
            const string head = "item.";
            if (itemKey == null || !itemKey.StartsWith(head, StringComparison.Ordinal)) return null;
            string name = itemKey.Substring(head.Length);
            return Array.IndexOf(SpotTokens, name) >= 0 ? ItemTokens + name + ".png" : null;
        }

        /// <summary>
        /// 그 아이템 키(<c>item.&lt;이름&gt;</c>)의 유물 아이콘 키(결정 80 L14 나 — 가방 · 정비 · 거래 창 · 카드 확대 · 숨겨진 장소 결과): 유물 = <c>artifact_&lt;이름&gt;.png</c>(비밀열쇠만 접두사 없음),
        /// 천상의 유물 = <c>&lt;금강·백금·백옥&gt;_orb.png</c>. 유물이 아니면(우두머리 포함) null.
        /// </summary>
        public static string RelicIconKey(string itemKey)
        {
            if (SpotTokenKey(itemKey) == null) return null;
            string name = itemKey.Substring("item.".Length);
            if (name == "alpha_pokemon") return null;
            const string celestial = "_artifact";
            string file = name.EndsWith(celestial, StringComparison.Ordinal) ? name.Substring(0, name.Length - celestial.Length) + "_orb" : name == "secret_key" ? name : "artifact_" + name;
            return ItemIcons + file + ".png";
        }

        static bool IsRelicIcon(string key)
        {
            foreach (string t in SpotTokens)
                if (RelicIconKey("item." + t) == key) return true;
            return false;
        }

        static bool IsSpotToken(string key)
        {
            if (key == null || !key.StartsWith(ItemTokens, StringComparison.Ordinal) || !key.EndsWith(".png", StringComparison.Ordinal)) return false;
            return Array.IndexOf(SpotTokens, key.Substring(ItemTokens.Length, key.Length - ItemTokens.Length - 4)) >= 0;
        }

        /// <summary>
        /// 화면이 읽는 아이템 그림인가: 볼 넷 · 규토리 일곱 색 · 지닌물건(battle_item_*) · 스튜 넷(stew_* — 제작 창 S26, M5) · 신오의 심장(HUD 3막 심장 회복 94행) · 수호신의 서(거래 창 칩 — 접두사 없는 파일, M5 마감 검토) · 유물 열하나(<see cref="RelicIconKey"/> — 결정 80 L14 나).
        /// 나머지 아이템 그림은 그 화면을 만들 때 더한다.
        /// </summary>
        public const string HeartIcon = "heart_of_sinnoh.png", ScrollIcon = "scroll_of_nobility.png";

        public static bool IsItemIcon(string key)
        {
            if (key == null || !key.StartsWith(ItemIcons, StringComparison.Ordinal)) return false;
            string name = key.Substring(ItemIcons.Length);
            if (name == HeartIcon || name == ScrollIcon || IsRelicIcon(key)) return true;
            return name.EndsWith("_ball.png", StringComparison.Ordinal) || ((name.StartsWith("apricorn_", StringComparison.Ordinal) || name.StartsWith("battle_item_", StringComparison.Ordinal) || name.StartsWith("stew_", StringComparison.Ordinal)) && name.EndsWith(".png", StringComparison.Ordinal));
        }

        /// <summary>읽을 준비가 됐나: 에디터 · Win64는 늘 true, 웹 손님판은 첫 받기 번들을 다 받은 뒤(<see cref="WebContent"/>).</summary>
        public static bool Ready =>
#if UNITY_WEBGL && !UNITY_EDITOR
            WebContent.Ready;
#else
            true;
#endif

        /// <summary>준비되면(이미 됐으면 바로) 한 번 부른다 — 보드 · 화면 시작이 기다린다.</summary>
        public static void WhenReady(Action action)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            WebContent.WhenReady(action);
#else
            action();
#endif
        }

        /// <summary>그 키의 번들을 아직 받는 중인가(웹 손님판의 나눠 받기 — 지금 없지만 곧 온다). 에디터 · Win64는 늘 false.</summary>
        public static bool Pending(string key)
        {
            if (Withheld?.Invoke(key) is bool held) return held;
#if UNITY_WEBGL && !UNITY_EDITOR
            return WebContent.Pending(BundleFor(key));
#else
            return false;
#endif
        }

        /// <summary>나눠 받기 번들 하나를 다 받았다(번들 이름 — 웹 손님판만 분다).</summary>
        public static event Action<string> Arrived;

        internal static void NoteArrived(string bundle) => Arrived?.Invoke(bundle);

        /// <summary>
        /// 시험용 갈고리(에디터에서 웹 손님판의 나눠 받기를 흉내 — M8 · M9 마감 감사 2차 참고 ③): 키마다 true = 아직 받는 중(<see cref="Pending"/> true · <see cref="Load{T}"/> null),
        /// false = 받았지만 없음(Load null), null = 그대로. 게임 코드는 쓰지 않는다 — 시험이 끝에 null로 되돌린다.
        /// </summary>
        public static Func<string, bool?> Withheld { get; set; }

        public static T Load<T>(string key) where T : UnityEngine.Object
        {
            if (Withheld?.Invoke(key) != null) return null;
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(Root + key);
#elif UNITY_WEBGL
            string name = BundleFor(key);
            var b = name != null ? WebContent.Loaded(name) : null;
            return b != null ? b.LoadAsset<T>(key) : null;
#else
            string name = BundleFor(key);
            if (name == null) return null;
            var b = Bundle(name);
            return b != null ? b.LoadAsset<T>(key) : null;
#endif
        }

#if !UNITY_EDITOR && !UNITY_WEBGL
        static readonly Dictionary<string, AssetBundle> loaded = new Dictionary<string, AssetBundle>(StringComparer.Ordinal);
        static AssetBundleManifest manifest;
        static bool manifestTried;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            loaded.Clear();
            manifest = null;
            manifestTried = false;
        }

        /// <summary>번들 하나를 (의존 번들 먼저) 한 번만 연다. 없으면 null을 기억해 다시 찾지 않는다.</summary>
        static AssetBundle Bundle(string name)
        {
            if (loaded.TryGetValue(name, out var b)) return b;
            string dir = Path.Combine(Application.streamingAssetsPath, ContentFolder);
            if (!manifestTried)
            {
                manifestTried = true;
                string mp = Path.Combine(dir, ContentFolder);
                var mb = File.Exists(mp) ? AssetBundle.LoadFromFile(mp) : null;
                if (mb != null) manifest = mb.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                if (manifest == null) Debug.LogWarning("[GenAssets] 번들 목록 없음: " + mp + " — Sinnoh.Editor.BoardBuild로 빌드했나");
            }
            string path = Path.Combine(dir, name);
            if (!File.Exists(path)) { loaded[name] = null; return null; }
            if (manifest != null)
                foreach (var dep in manifest.GetAllDependencies(name))
                    if (!loaded.ContainsKey(dep)) loaded[dep] = AssetBundle.LoadFromFile(Path.Combine(dir, dep));
            b = AssetBundle.LoadFromFile(path);
            loaded[name] = b;
            return b;
        }
#endif
    }
}
