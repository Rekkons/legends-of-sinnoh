using System;
using System.Collections.Generic;
using System.Globalization;
using Sinnoh.Fx;
using Sinnoh.Rules;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sinnoh.Client
{
    /// <summary>DS 화면에 세울 포켓몬 하나(체력 상자 값 포함). 값은 화면이 넣는다 — 여기서 규칙을 읽지 않는다.</summary>
    public sealed class DsMon
    {
        /// <summary>pokemon.json 키(「dex:state」) — 정지 도트 표의 키.</summary>
        public string Key;
        public string Name;
        /// <summary>이름이 칸에 들지 않으면 자르지 않고 글자 폭을 좁힌다(결정 87 V22 나 — 「울트라네크로즈마」만).</summary>
        public bool Narrow;
        public int Hp, MaxHp, Speed, Fatigue;
        public StatusEffect Status;
        /// <summary>둘째 상태 이상(없으면 None) — 체력 상자 바깥 가장자리에 붙는 배지.</summary>
        public StatusEffect Status2;
        /// <summary>도트를 그리나(기절하면 상자만 남는다).</summary>
        public bool Standing = true;
        /// <summary>연출이 옮긴 만큼(DS px): 도트(<see cref="Dx"/>·<see cref="Dy"/>)와 체력 상자(<see cref="BoxDy"/> — 아래로).</summary>
        public int Dx, Dy, BoxDy;
        /// <summary>도트가 발판 아래로 내려간 만큼(0 ~ 1 — 발 줄 아래는 잘린다. 기절).</summary>
        public float Sink;
        /// <summary>체력 막대만 따로 그릴 값(깎이는 중 — 0 미만이면 <see cref="Hp"/> 그대로). 숫자는 늘 <see cref="Hp"/>다.</summary>
        public float Bar = -1f;
        /// <summary>체력 상자를 옆으로 옮긴 만큼과 그리나(인트로 — 도트가 나온 뒤 미끄러져 들어온다).</summary>
        public int BoxDx;
        public bool BoxShown = true;
        /// <summary>도트를 하얗게 물들인 만큼(0 ~ 1)과 크기(1 = 그대로) — 볼에 빨려 들고 튀어나올 때. 줄어드는 중심 = 발 위 <see cref=ShrinkAbove/>px(볼이 열린 자리).</summary>
        public float White, Scale = 1f;
        public int ShrinkAbove;
        /// <summary>도트 둘레의 오라(M5 「맵 결과·배틀 안 연출」 — 76·77·78·79행 · 팬 폼): 실루엣을 이 색으로 둘레 AuraPx에 찍는다(0 = 없음). Tint = 도트를 그 색으로 물들인다(알파 = 세기).</summary>
        public Color32 Aura, Tint;
        public int AuraPx;
        /// <summary>체력 상자 옆의 작은 그림(결정 80 L16 나): 은하의 응징이 쌓인 수(0 = 그리지 않는다 — 상대 상자만) · 교체 불가 자물쇠. 상자와 함께 움직인다.</summary>
        public int Retribution;
        public bool Locked;
    }

    /// <summary>DS 화면에 찍을 그림 조각 하나(주인공 뒷모습 · 상대 트레이너 · 볼): 그림 키와 그 안 조각, 화면의 왼쪽 위 자리.</summary>
    public sealed class DsSprite
    {
        public string Key;
        public int Sx, Sy, Sw, Sh;
        public int X, Y;
        /// <summary>어둡게(포획 성공의 딸깍).</summary>
        public bool Dark;
    }

    /// <summary>
    /// 우선권 토큰(결정 73 ⑦ — 04 §4-2 「상자 옆」): 16×16 그림의 왼쪽 위 자리, 도는 동전의 보이는 폭(px, 가운데 맞춤 — 0이면 그리지 않는다)과 면,
    /// 하얗게 빛나는 만큼(0 ~ 1)과 둘레 반짝이 자리(0 ~ 3, 없으면 −1). 체력 상자보다 앞, 메시지 창보다 뒤.
    /// </summary>
    public sealed class DsRect
    {
        public int X, Y, W, H;
        public Color32 Color;
    }

    public sealed class DsToken
    {
        public int X, Y, Width = DsStage.TokenSize;
        public bool Back;
        public float White;
        public int Twinkle = -1;
    }

    /// <summary>DS 화면 한 장에 그릴 것(04 §4-2): 무대 그림 셋 · 양쪽 포켓몬 · 메시지 창 글(두 줄까지).</summary>
    public sealed class DsScene
    {
        public string Bg, EnemyPlatform, PlayerPlatform;
        public DsMon Enemy, Player;
        public string Message;
        /// <summary>내 주인공 뒷모습(내 포켓몬 뒤에 찍는다 — 포획) · 상대 트레이너 앞모습(갤럭시단 등장) · 볼(포켓몬 위에 찍는다).</summary>
        public DsSprite Back, Foe, Ball;
        /// <summary>볼 둘레 별 수(0 ~ 3, 포획 성공)와 볼 가운데 자리.</summary>
        public int Stars;
        public Vector2Int StarsAt;
        /// <summary>도트 이펙트 조각(결정 69 — 맞는 불꽃 · 볼 열림 빛 · 먼지 · 반짝임): 포켓몬·볼보다 앞, 체력 상자·메시지 창보다 뒤에 차례로 찍는다.</summary>
        public List<DsSprite> Fx;
        /// <summary>단색 네모(발판 뒤 · 포켓몬 앞 — 신 둘째 폼의 빛기둥 78행). 알파 그대로 섞는다.</summary>
        public List<DsRect> Rects;
        /// <summary>우선권 토큰(없으면 null).</summary>
        public DsToken Token;
        /// <summary>공간 일그러짐(M9 다듬기 P39 — 기라티나의 그림자 · 울트라 비스트 배틀의 균열): 다 그린 화면을 줄마다 가로로 미는 진폭(DS px, 0 = 없음)과 위상(0 ~ 1). 메시지 창 줄은 그대로 둔다.</summary>
        public float WarpPx, WarpPhase;
    }

    /// <summary>
    /// DS 층(04 §4-2·§9-1 — UI Toolkit으로 그리지 않는다): 256×192 RenderTexture(Point, 후처리 없음)에 Gen 그림을 직접 찍는다.
    /// 무대 배경 → 발판 둘 → 상대 앞모습 → 내 뒷모습 → 체력 상자 둘(이름 · 스피드 · HP 막대 · 「남은/최대」 · 전투 피로) → 메시지 창.
    /// 결과 <see cref="Output"/>을 PC 층 요소의 배경으로 끼운다(<c>Background.FromRenderTexture</c>) — 요소 크기 = 256×192의 정수배(04 §4-3).
    /// 자리 값은 모두 DS px(왼쪽 위가 (0, 0), y 아래로)이고 이 파일의 상수다 — PC 층 토큰(USS)이 아니라 DS 화면의 그림 배치다.
    /// 움직임은 장면 값이 싣는다(도트·상자를 옮긴 만큼 · 내려간 만큼 · 깎이는 막대 — <see cref="DsMon"/>): 연출이 도는 동안 화면이 <see cref="Draw"/>를 프레임마다 부른다. 여기에는 시간이 없다. 씬·프리팹을 쓰지 않고 Gen을 직렬화로 가리키지 않는다.
    /// </summary>
    public sealed class DsStage : IDisposable
    {
        public const int Width = 256, Height = 192;
        public const string ShaderName = "Sinnoh/DsBlit";
        /// <summary>공간 일그러짐의 뒤처리 셰이더(M9 다듬기 P39 — 없으면 일그러짐 없이 그린다).</summary>
        public const string WarpShaderName = "Sinnoh/DsWarp";

        // ── 그림 키 ──
        public const string StageTableKey = "Stage/stage.json";
        public const string EnemyBoxKey = "Stage/Hud/healthbox_enemy.png", PlayerBoxKey = "Stage/Hud/healthbox_player_singles.png", DigitsKey = "Stage/Hud/special_chars.png";
        public const string MessageBoxKey = "Stage/Window/message_box_00.png";
        public const string HudFont = "pt_system", MessageFont = "pt_message";
        public static string StatusKey(StatusEffect s) => IconArt.Status(s);   // 언어별 그림(결정 115)
        /// <summary>볼 던지기 그림(Gen <c>Stage/Ball/&lt;아이템&gt;.png</c> = 도는 프레임 9장 × <see cref="BallCell"/>px 가로, <c>&lt;아이템&gt;_open.png</c> = 열린 볼 <see cref="BallOpen"/>px).</summary>
        public const string BallFolder = "Stage/Ball/";
        public const int BallCell = 16, BallOpen = 32;
        /// <summary>우선권 토큰 앞면·뒷면(Gen — 원작 TTS 토큰을 도트로 다시 그린 16×16, stage 묶음).</summary>
        public const string TokenKey = "Stage/Hud/initiative_token.png", TokenBackKey = "Stage/Hud/initiative_token_back.png";
        public const int TokenSize = 16;

        // ── 자리(DS px) ──
        /// <summary>발판 고정점(04 §4-5): 도트의 가장 아래 불투명 줄이 여기에 선다.</summary>
        public static readonly Vector2Int EnemyFoot = new Vector2Int(192, 92), PlayerFoot = new Vector2Int(64, 146);
        /// <summary>발판 그림의 왼쪽 위. 상대 128×64는 고정점이 타원 가운데 조금 아래에 오게, 내 쪽 256×32는 왼쪽으로 밀어 아래를 메시지 창에 잘리게(원작 자산 셀 그대로) [가정 — 캡처로 맞춤].</summary>
        public static readonly Vector2Int EnemyPlatformAt = new Vector2Int(128, 54), PlayerPlatformAt = new Vector2Int(-60, 128);
        /// <summary>체력 상자 그림(128×64)의 왼쪽 위. 상대 상자는 숫자 칸을 넣으려고 아래로 <see cref="EnemyBoxGrow"/>줄 늘린다(04 §4-2 「Pt 적 상자에는 숫자 칸이 없어 새로 넣는다」).</summary>
        public static readonly Vector2Int EnemyBoxAt = new Vector2Int(-3, 5), PlayerBoxAt = new Vector2Int(128, 86);
        public const int EnemyBoxGrow = 8;
        /// <summary>
        /// 우선권 토큰의 자리(16×16의 왼쪽 위 — 체력 상자 그림의 왼쪽 위에서 본 값, 상자와 함께 움직인다): 상대 상자는 이름 칸 오른쪽, 내 상자는 이름 칸 왼쪽(화살표 위) —
        /// 04 §4-2 「상자 옆」 [가정 — 캡처로 맞춤, 포켓몬 도트를 덜 가리는 쪽].
        /// </summary>
        public static readonly Vector2Int EnemyTokenFromBox = new Vector2Int(119, 14), PlayerTokenFromBox = new Vector2Int(2, 14);
        /// <summary>
        /// 내 주인공 뒷모습의 화면 고정점(시트의 원점 <c>origin</c>이 놓이는 점 — 결정 55 ①): 왼쪽 가장자리에 반쯤 걸쳐 선다(목업 r5 [가정 — 캡처로 맞춤]).
        /// 층은 내 포켓몬보다 앞, 볼·메시지 창보다 뒤다 — 뒷모습이 나와 있는 동안 체력 상자 둘은 화면이 숨긴다(결정 62, <c>CaptureStage.Pose</c>의 <c>BoxHidden</c>·<c>BoxAway</c>). 볼이 손을 떠나는 자리는 주인공마다 표의 <c>hand</c>(<c>BackThrow.HandAt</c>)이고,
        /// <see cref="HandFrom"/>은 뒷모습 표가 없을 때만 쓰는 4세대 손 자리(고정점에서 본 값)다. 상대 트레이너 앞모습(80×80)은 발이 상대 발판 고정점에 온다.
        /// </summary>
        public static readonly Vector2Int TrainerAt = new Vector2Int(22, 144), HandFrom = new Vector2Int(30, -54);
        /// <summary>메시지 창(아래 48줄)과 글 자리.</summary>
        public static readonly RectInt MessageRect = new RectInt(0, 144, 256, 48);
        public const int MessageTextX = 16, MessageTextY = 152, MessageLine = 16, MessageTextRight = 232;

        // 상자 안 자리(상자 그림의 왼쪽 위에서)
        const int NameY = 15, SpeedY = 19, BarY = 35, BarW = 48, BarH = 2, NumY = 40, DigitW = 8;
        const int EnemyNameX = 9, EnemyRight = 110, EnemyBarX = 56;
        const int PlayerNameX = 24, PlayerRight = 124, PlayerBarX = 72;
        /// <summary>
        /// 상자 바깥에 붙는 상태 배지(32×16 — 둘째 상태 · 이름 옆에 못 들어간 첫 상태)의 첫 자리(상자 그림의 왼쪽 위에서): 상대 상자는 늘린 아래 테두리 바로 밑(이름 줄에 맞춰, 더 있으면 오른쪽으로),
        /// 내 상자는 왼쪽 테두리 바깥 · 우선권 토큰 자리 바로 위(더 있으면 위로) [가정 — 캡처로 맞춤].
        /// </summary>
        public static readonly Vector2Int EnemyBadgeOut = new Vector2Int(EnemyNameX, 54), PlayerBadgeOut = new Vector2Int(-15, -3);
        const int FatigueLineY = 50, FatigueLineX = 24, FatigueSeg = 16, FatigueGap = 4;
        /// <summary>
        /// 체력 상자 옆의 작은 그림(결정 80 L16 나 — 상자 그림의 왼쪽 위에서 본 자리): 교체 불가 자물쇠(<see cref="LockW"/>×<see cref="LockH"/>)는 상대 상자 바로 위의 오른쪽 끝 · 내 상자 왼쪽(우선권 토큰 자리의 왼쪽),
        /// 은하의 응징 구슬(<see cref="OrbSize"/> + 숫자)은 상대 상자 위에서 자물쇠 자리의 왼쪽. 상대 상자 아래 줄은 내 포켓몬의 큰 뒷모습(디아루가 오리진폼의 머리)이 올라와 겹친다 — 상자 위는 늘 하늘이다.
        /// 바깥 상태 배지(<see cref="EnemyBadgeOut"/>·<see cref="PlayerBadgeOut"/>)·토큰과 겹치지 않는다 [가정 — 캡처로 맞춤].
        /// </summary>
        public static readonly Vector2Int EnemyLockFromBox = new Vector2Int(EnemyRight - LockW, 0), PlayerLockFromBox = new Vector2Int(-13, 16);
        public const int LockW = 11, LockH = 12, OrbSize = 9;

        /// <summary>자물쇠 그림의 자리(DS px — 상자가 제자리일 때). 마우스 오버 칸이 읽는다.</summary>
        public static RectInt LockRect(bool enemy)
        {
            var at = enemy ? EnemyBoxAt : PlayerBoxAt;
            var from = enemy ? EnemyLockFromBox : PlayerLockFromBox;
            return new RectInt(at.x + from.x, at.y + from.y, LockW, LockH);
        }

        /// <summary>응징 구슬 + 숫자의 자리(DS px — 상대 상자 위, 자물쇠 자리의 왼쪽).</summary>
        public static RectInt RetributionRect(int stored)
        {
            int w = OrbSize + 1 + Digits(stored) * DigitW;
            return new RectInt(EnemyBoxAt.x + EnemyLockFromBox.x - 2 - w, EnemyBoxAt.y + EnemyLockFromBox.y, w, LockH);
        }

        // ── 색(sRGB) ──
        static readonly Color32 White = new Color32(0xFF, 0xFF, 0xFF, 0xFF), BoxShadow = new Color32(0x29, 0x31, 0x29, 0xFF), BoxFill = new Color32(0x6A, 0x73, 0x5A, 0xFF);
        static readonly Color32 MsgInk = new Color32(0x5A, 0x5A, 0x52, 0xFF), MsgShadow = new Color32(0xAC, 0xBD, 0xBD, 0xFF);
        static readonly Color32 HpGreen = new Color32(0x18, 0xC5, 0x20, 0xFF), HpYellow = new Color32(0xEE, 0xAC, 0x00, 0xFF), HpRed = new Color32(0xFF, 0x41, 0x10, 0xFF);
        static readonly Color32 FatigueOff = new Color32(0xAC, 0xAC, 0x94, 0xFF);
        static readonly Color32 StarInk = new Color32(0xFF, 0xE6, 0x4A, 0xFF), StarEdge = new Color32(0x8B, 0x62, 0x10, 0xFF);
        static readonly Vector2Int[] StarOffsets = { new Vector2Int(-12, -7), new Vector2Int(0, -14), new Vector2Int(12, -7) };
        // 토큰 둘레 반짝이 자리(토큰 왼쪽 위에서): 오른쪽 위 → 오른쪽 아래 → 왼쪽 아래 → 왼쪽 위로 돈다
        static readonly Vector2Int[] TokenTwinkles = { new Vector2Int(14, 1), new Vector2Int(14, 14), new Vector2Int(1, 14), new Vector2Int(1, 1) };

        // 픽셀 ⌃⌃(스피드, 04 §10-1 — 7×7, 위에서부터 한 줄씩)
        static readonly string[] Chevrons = { "...#...", "..#.#..", ".#...#.", "...#...", "..#.#..", ".#...#.", "......." };
        // 픽셀 자물쇠(교체 불가)와 응징 구슬(결정 80 L16 나): # 테두리 · s 은빛 고리 · o 금빛 몸 / r 붉은 구슬 · w 빛 · d 그늘
        static readonly string[] LockArt = { "...#####...", "..#sssss#..", "..#s###s#..", "..#s#.#s#..", ".#########.", "#ooooooooo#", "#oooo#oooo#", "#ooo###ooo#", "#oooo#oooo#", "#oooo#oooo#", "#ooooooooo#", ".#########." };
        static readonly string[] OrbArt = { "..#####..", ".#rrrrr#.", "#rwwrrrr#", "#rwrrrrr#", "#rrrrrrr#", "#rrrrrrd#", "#rrrrrdd#", ".#rrddd#.", "..#####.." };
        static readonly Color32 LockRing = new Color32(0xD8, 0xDC, 0xE8, 0xFF), LockBody = new Color32(0xF8, 0xC8, 0x48, 0xFF);
        static readonly Color32 OrbRed = new Color32(0xE0, 0x30, 0x28, 0xFF), OrbLight = new Color32(0xFF, 0xB8, 0xA8, 0xFF), OrbDark = new Color32(0x98, 0x18, 0x18, 0xFF);

        [Serializable] sealed class TerrainRow { public string biome; public string climate; public bool distortion; public string bg; public string enemy; public string player; }
        [Serializable] sealed class StageTable { public TerrainRow[] terrain; public string[] timed; }

        public readonly RenderTexture Output;

        /// <summary>마지막 <see cref="Draw"/>가 화면을 일그러뜨린 진폭(DS px — 0 = 그대로). 확인용.</summary>
        public float WarpDrawn { get; private set; }

        // 일그러뜨릴 때만 쓰는 밑그림(처음 필요할 때 만든다)과 뒤처리 재질
        RenderTexture plain;
        readonly Material warp;
        static readonly int SizeId = Shader.PropertyToID("_Size"), WarpId = Shader.PropertyToID("_Warp");

        /// <summary>마지막 <see cref="Draw"/>가 실제로 그린 트레이너 뒷모습의 Gen 키(장면에 없거나 그림을 읽지 못했으면 null) — 확인 도구가 「고른 주인공이 그려졌나」를 본다.</summary>
        public string BackDrawn { get; private set; }

        /// <summary>마지막 <see cref="Draw"/>에서 잘린 글(체력 상자 이름이 폭을 넘어 「…」로 줄었다 · 메시지가 두 줄을 넘었다 · 한 줄이 창 폭을 넘었다) — 없으면 빈 목록. 캡처·시험이 단정한다.</summary>
        public readonly List<string> Cut = new List<string>();

        readonly Material material;
        readonly Mesh quad;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly DsFont hudFont, messageFont;
        readonly MonArt mons;
        readonly Texture2D enemyBox, playerBox, digits, messageBox;
        readonly Dictionary<string, Texture2D> loaded = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        readonly Matrix4x4 viewProj;
        CommandBuffer cmd;

        static readonly int TexId = Shader.PropertyToID("_DsTex"), VpId = Shader.PropertyToID("_DsVP"), RectId = Shader.PropertyToID("_Rect"), SrcId = Shader.PropertyToID("_Src"),
            InkId = Shader.PropertyToID("_Ink"), ShadowId = Shader.PropertyToID("_Shadow"), ModeId = Shader.PropertyToID("_Mode");

        DsStage(Shader shader, DsFont hudFont, DsFont messageFont, MonArt mons, Texture2D enemyBox, Texture2D playerBox, Texture2D digits, Texture2D messageBox)
        {
            this.hudFont = hudFont;
            this.messageFont = messageFont;
            this.mons = mons;
            this.enemyBox = enemyBox;
            this.playerBox = playerBox;
            this.digits = digits;
            this.messageBox = messageBox;
            material = new Material(shader) { name = "DsBlit", hideFlags = HideFlags.DontSave };
            var warpShader = Shader.Find(WarpShaderName);
            if (warpShader != null) warp = new Material(warpShader) { name = "DsWarp", hideFlags = HideFlags.DontSave };
            quad = new Mesh { name = "DsQuad", hideFlags = HideFlags.DontSave };
            quad.vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) };
            quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            Output = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "DsStage", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, antiAliasing = 1, useMipMap = false, hideFlags = HideFlags.DontSave,
            };
            Output.Create();
            // y 아래로 가는 DS px → 클립. RT에 그리므로 플랫폼의 위아래 뒤집기를 넣는다(PC 층이 이 RT를 그림으로 읽을 때 바로 선다)
            viewProj = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0, Width, Height, 0, -1, 1), true);
        }

        /// <summary>DS 층을 만든다. Gen 본체(글꼴·체력 상자·창틀·정지 도트)나 셰이더가 없으면 null.</summary>
        public static DsStage Create()
        {
            var shader = Shader.Find(ShaderName);
            var hud = DsFont.Load(HudFont);
            var msg = DsFont.Load(MessageFont);
            var mons = MonArt.Load();
            Texture2D e = GenAssets.Load<Texture2D>(EnemyBoxKey), p = GenAssets.Load<Texture2D>(PlayerBoxKey), d = GenAssets.Load<Texture2D>(DigitsKey), m = GenAssets.Load<Texture2D>(MessageBoxKey);
            if (shader == null || hud == null || msg == null || mons == null || e == null || p == null || d == null || m == null) return null;
            return new DsStage(shader, hud, msg, mons, e, p, d, m);
        }

        public void Dispose()
        {
            if (cmd != null) cmd.Release();
            cmd = null;
            if (Output != null)
            {
                Output.Release();
                Kill(Output);
            }
            if (plain != null)
            {
                plain.Release();
                Kill(plain);
            }
            Kill(warp);
            Kill(material);
            Kill(quad);
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        // ── 무대 짝 ──

        /// <summary>
        /// 배틀 칸의 지형·기후 → 무대 그림 셋(배경 · 상대 발판 · 내 발판, Gen <c>Stage/stage.json</c>의 <c>terrain</c> 표 — 03 §2-3).
        /// 맞는 줄이 없으면 테마 없는 칸의 줄(지형·기후 없음), 그것도 없으면 첫 줄. 표가 없으면 false.
        /// </summary>
        public static bool Terrain(Biome biome, Climate climate, out string bg, out string enemy, out string player) => Terrain(biome, climate, DayTime.Day, out bg, out enemy, out player);

        /// <summary>시간대까지 고른 무대 그림 셋(결정 70 ⑥): 표의 <c>timed</c>에 있는 그림(야외 배경 여섯 · 발판 아홉 지형)만 저녁·밤 그림으로 바뀐다 — 동굴·깨어진 세계는 그대로.</summary>
        public static bool Terrain(Biome biome, Climate climate, DayTime time, out string bg, out string enemy, out string player)
        {
            bg = enemy = player = null;
            var json = GenAssets.Load<TextAsset>(StageTableKey);
            if (json == null) return false;
            var table = JsonUtility.FromJson<StageTable>(json.text);
            if (table == null || table.terrain == null || table.terrain.Length == 0) return false;
            TerrainRow pick = null, plain = null;
            foreach (var r in table.terrain)
            {
                if (r.distortion) continue;
                if (string.IsNullOrEmpty(r.biome) && string.IsNullOrEmpty(r.climate)) plain = plain ?? r;
                else if (r.biome == biome.ToString() && r.climate == climate.ToString()) pick = pick ?? r;
            }
            pick = pick ?? plain ?? table.terrain[0];
            string Timed(string file) => "Stage/" + DayTimes.Art(file, time, table.timed != null && Array.IndexOf(table.timed, file) >= 0);
            bg = Timed(pick.bg);
            enemy = Timed(pick.enemy);
            player = Timed(pick.player);
            return true;
        }

        // ── 그리기 ──

        /// <summary>장면 한 장을 <see cref="Output"/>에 처음부터 다시 그린다.</summary>
        public void Draw(DsScene scene)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            if (!Output.IsCreated()) Output.Create();
            if (cmd == null) cmd = new CommandBuffer { name = "DsStage" };
            cmd.Clear();
            Cut.Clear();
            NarrowShown = false;
            // 공간 일그러짐: 밑그림에 그린 뒤 줄마다 밀어 Output에 옮겨 찍는다(없으면 Output에 바로)
            bool warped = warp != null && scene.WarpPx >= 0.5f;
            WarpDrawn = warped ? scene.WarpPx : 0f;
            if (warped && plain == null)
            {
                plain = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "DsStagePlain", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, antiAliasing = 1, useMipMap = false, hideFlags = HideFlags.DontSave,
                };
            }
            if (warped && !plain.IsCreated()) plain.Create();
            cmd.SetRenderTarget(warped ? plain : Output);
            cmd.ClearRenderTarget(false, true, new Color(0, 0, 0, 1));
            cmd.SetGlobalMatrix(VpId, viewProj);

            Whole(Tex(scene.Bg), 0, 0);
            Whole(Tex(scene.EnemyPlatform), EnemyPlatformAt.x, EnemyPlatformAt.y);
            Whole(Tex(scene.PlayerPlatform), PlayerPlatformAt.x, PlayerPlatformAt.y);
            Sprite(scene.Foe);
            if (scene.Rects != null) foreach (var r in scene.Rects) Fill(r.X, r.Y, r.W, r.H, r.Color);
            Mon(scene.Enemy, false, EnemyFoot);
            Mon(scene.Player, true, PlayerFoot);
            // 주인공 뒷모습: 내 포켓몬보다 앞, 볼·메시지 창보다 뒤. 체력 상자는 그동안 장면이 숨긴다(결정 62)
            Sprite(scene.Back);
            BackDrawn = scene.Back != null && Tex(scene.Back.Key) != null ? scene.Back.Key : null;
            Sprite(scene.Ball);
            for (int i = 0; i < scene.Stars && i < StarOffsets.Length; i++)
            {
                // 별: 십자 다섯 칸 + 어두운 가운데 테두리 없이(3×3)
                int x = scene.StarsAt.x + StarOffsets[i].x, y = scene.StarsAt.y + StarOffsets[i].y;
                Fill(x - 2, y, 5, 1, StarEdge);
                Fill(x, y - 2, 1, 5, StarEdge);
                Fill(x - 1, y, 3, 1, StarInk);
                Fill(x, y - 1, 1, 3, StarInk);
            }
            if (scene.Fx != null) foreach (var fx in scene.Fx) Sprite(fx);
            if (scene.Enemy != null && scene.Enemy.BoxShown) Box(scene.Enemy, true);
            if (scene.Player != null && scene.Player.BoxShown) Box(scene.Player, false);
            Token(scene.Token);
            Message(scene.Message);

            if (warped)
            {
                warp.SetTexture(TexId, plain);
                warp.SetVector(SizeId, new Vector4(Width, Height, 0, 0));
                warp.SetVector(WarpId, new Vector4(scene.WarpPx, scene.WarpPhase, RippleBeats.DsWaveRows, RippleBeats.DsKeepRows));
                cmd.SetRenderTarget(Output);
                cmd.DrawProcedural(Matrix4x4.identity, warp, 0, MeshTopology.Triangles, 3);
            }
            Graphics.ExecuteCommandBuffer(cmd);
        }

        Texture2D Tex(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!loaded.TryGetValue(key, out var t)) loaded[key] = t = GenAssets.Load<Texture2D>(key);
            return t;
        }

        void Mon(DsMon mon, bool back, Vector2Int foot)
        {
            if (mon == null || !mon.Standing || mon.Key == null) return;
            if (!mons.TryPiece(mon.Key, back, out var page, out var rect, out var pivot)) return;
            // 내려가는 도트(기절): 발 줄 위의 줄만 남기고 그만큼 아래로 민다 — 발 줄 아래로 나간 것은 그리지 않는다
            int rows = Math.Min(rect.height, Math.Max(0, pivot.y)), down = Mathf.Clamp(Mathf.RoundToInt(mon.Sink * rows), 0, rows);
            int h = mon.Sink > 0f ? rows - down : rect.height;
            if (h <= 0) return;
            int x = foot.x - pivot.x + mon.Dx, y = foot.y - pivot.y + mon.Dy + down, w = rect.width;
            var white = new Color32(0xFF, 0xFF, 0xFF, (byte)Mathf.RoundToInt(Mathf.Clamp01(mon.White) * 255f));
            if (mon.Scale < 1f)
            {
                // 볼 쪽으로 줄어든다: 발 위 ShrinkAbove 점을 중심으로
                float s = Mathf.Max(0f, mon.Scale);
                int cx = foot.x + mon.Dx, cy = foot.y - mon.ShrinkAbove + mon.Dy;
                int sw = Mathf.Max(1, Mathf.RoundToInt(w * s)), sh = Mathf.Max(1, Mathf.RoundToInt(h * s));
                Stamp(page, rect.x, rect.y, rect.width, h, cx + Mathf.RoundToInt((x - cx) * s), cy + Mathf.RoundToInt((y - cy) * s), sw, sh, 4, white, white);
                return;
            }
            if (mon.AuraPx > 0 && mon.Aura.a > 0)
            {
                // 오라: 실루엣을 둘레 여덟 방향으로 한 색으로 찍는다(그 위에 도트). 두께만큼 안쪽 고리도 채운다 — 바깥 고리만 찍으면 가는 선(뿔·꼬리) 둘레가 비어 얇아 보인다(결정 80 L15 나)
                var ink = new Color32(mon.Aura.r, mon.Aura.g, mon.Aura.b, 0xFF);
                for (int r = mon.AuraPx; r >= 1; r--)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (dx != 0 || dy != 0) Stamp(page, rect.x, rect.y, rect.width, h, x + dx * r, y + dy * r, w, h, 4, ink, ink);
            }
            bool tint = mon.White <= 0f && mon.Tint.a > 0;
            Stamp(page, rect.x, rect.y, rect.width, h, x, y, w, h, mon.White > 0f || tint ? 4 : 0, tint ? mon.Tint : white, tint ? mon.Tint : white);
        }

        // 그림 조각 하나(주인공 뒷모습 · 상대 트레이너 · 볼)
        void Sprite(DsSprite s)
        {
            if (s == null) return;
            var tex = Tex(s.Key);
            if (tex == null) return;
            var ink = s.Dark ? new Color32(0, 0, 0, 0x70) : new Color32(0, 0, 0, 0);
            Stamp(tex, s.Sx, s.Sy, s.Sw, s.Sh, s.X, s.Y, s.Sw, s.Sh, s.Dark ? 4 : 0, ink, ink);
        }

        // 우선권 토큰: 폭을 줄여 가운데에 찍고(도는 동전) 하얗게 덮는다. 반짝이는 네 모서리 가운데 하나에 별(포획 별과 같은 십자)
        void Token(DsToken token)
        {
            if (token == null || token.Width <= 0) return;
            var tex = Tex(token.Back ? TokenBackKey : TokenKey);
            if (tex == null) return;
            int w = Math.Min(TokenSize, token.Width), x = token.X + (TokenSize - w) / 2;
            var white = new Color32(0xFF, 0xFF, 0xFF, (byte)Mathf.RoundToInt(Mathf.Clamp01(token.White) * 255f));
            Stamp(tex, 0, 0, tex.width, tex.height, x, token.Y, w, TokenSize, token.White > 0f ? 4 : 0, white, white);
            if (token.Twinkle < 0 || token.Twinkle >= TokenTwinkles.Length) return;
            int sx = token.X + TokenTwinkles[token.Twinkle].x, sy = token.Y + TokenTwinkles[token.Twinkle].y;
            Fill(sx - 2, sy, 5, 1, StarEdge);
            Fill(sx, sy - 2, 1, 5, StarEdge);
            Fill(sx - 1, sy, 3, 1, StarInk);
            Fill(sx, sy - 1, 1, 3, StarInk);
        }

        // 체력 상자 하나: 틀 → 이름(+ 상태 배지) → ⌃⌃ 스피드 → HP 막대 → 전투 피로 n/5 · 「남은/최대」
        void Box(DsMon mon, bool enemy)
        {
            var at = enemy ? EnemyBoxAt : PlayerBoxAt;
            at.y += mon.BoxDy;
            at.x += mon.BoxDx;
            int grow = enemy ? EnemyBoxGrow : 0;
            int nameX = at.x + (enemy ? EnemyNameX : PlayerNameX), right = at.x + (enemy ? EnemyRight : PlayerRight), barX = at.x + (enemy ? EnemyBarX : PlayerBarX);
            if (enemy)
            {
                // Pt 상대 상자: 막대 아래가 두 줄뿐이다 — 그 줄을 늘려 숫자 칸을 만든다(위 41줄 그대로 · 41번째 줄을 grow줄 · 나머지는 그만큼 아래로)
                const int cut = 41;
                Piece(enemyBox, 0, 0, enemyBox.width, cut, at.x, at.y, enemyBox.width, cut);
                Piece(enemyBox, 0, cut - 1, enemyBox.width, 1, at.x, at.y + cut, enemyBox.width, grow);
                Piece(enemyBox, 0, cut, enemyBox.width, enemyBox.height - cut, at.x, at.y + cut + grow, enemyBox.width, enemyBox.height - cut);
            }
            else
            {
                Whole(playerBox, at.x, at.y);
                Fill(at.x + 86, at.y + 40, 10, 8, BoxFill);   // 그림에 박힌 「/」를 지운다(숫자를 직접 찍는다)
            }

            // 이름 + 상태 배지(이름 옆 32×16 — 작은 배지 그림이 없다, 03 §8 #21 [가정])
            int speedW = 8 + Digits(mon.Speed) * DigitW;
            int x;
            if (mon.Narrow && !string.IsNullOrEmpty(mon.Name) && hudFont.Width(mon.Name) > NameWidth(enemy, mon.Speed))
            {
                x = TextNarrow(hudFont, mon.Name, nameX, at.y + NameY, NameWidth(enemy, mon.Speed), White, BoxShadow);
                NarrowShown = true;
            }
            else
            {
                string fitted = Fit(hudFont, mon.Name, NameWidth(enemy, mon.Speed));
                if (fitted != (mon.Name ?? "")) Cut.Add("체력 상자 이름 「" + mon.Name + "」 → 「" + fitted + "」");
                x = Text(hudFont, fitted, nameX, at.y + NameY, right - speedW - 2, White, BoxShadow);
            }
            // 이름 옆에 들어가지 않는 배지(긴 이름 · 두 자리 스피드)와 둘째 상태 배지(상태 둘 — rulings G20)는 상자 바깥에 붙인다 — 상자와 함께 움직인다.
            // 상대 = 상자 아래에 왼쪽부터 옆으로, 내 쪽 = 상자 왼쪽(우선권 토큰 자리 위)에 아래부터 위로(상자 위에 두면 상대 도트의 발을 가린다)
            int outside = 0;
            if (mon.Status != StatusEffect.None)
            {
                var badge = Tex(StatusKey(mon.Status));
                if (badge != null && x + 1 + badge.width <= right - speedW) Whole(badge, x + 1, at.y + NameY);
                else if (badge != null) Outside(badge, at, enemy, outside++);
            }
            if (mon.Status != StatusEffect.None && mon.Status2 != StatusEffect.None)
            {
                var badge = Tex(StatusKey(mon.Status2));
                if (badge != null) Outside(badge, at, enemy, outside);
            }
            // 응징 · 교체 불가(결정 80 L16 나): 상자 옆의 작은 그림
            if (mon.Locked)
            {
                var from = enemy ? EnemyLockFromBox : PlayerLockFromBox;
                Glyph(LockArt, at.x + from.x, at.y + from.y);
            }
            if (enemy && mon.Retribution > 0)
            {
                int ox = at.x + EnemyLockFromBox.x - 2 - (OrbSize + 1 + Digits(mon.Retribution) * DigitW), oy = at.y + EnemyLockFromBox.y;
                Glyph(OrbArt, ox, oy + 1);
                NumberLeft(mon.Retribution.ToString(CultureInfo.InvariantCulture), ox + OrbSize + 1, oy + 2, White, BoxShadow);
            }
            // 스피드: 픽셀 ⌃⌃ + 숫자(Lv 자리, 04 §4-2)
            int sx = right - speedW;
            for (int r = 0; r < Chevrons.Length; r++)
                for (int c = 0; c < Chevrons[r].Length; c++)
                    if (Chevrons[r][c] == '#')
                    {
                        Fill(sx + c + 1, at.y + SpeedY + r + 1, 1, 1, BoxShadow);
                        Fill(sx + c, at.y + SpeedY + r, 1, 1, White);
                    }
            Number(mon.Speed.ToString(CultureInfo.InvariantCulture), right, at.y + SpeedY, White, BoxShadow);

            // HP 막대: 48칸 중 남은 만큼(0보다 크면 1칸은 남긴다 — Pt), 반 이하 노랑 · 1/5 이하 빨강
            int max = Math.Max(1, mon.MaxHp), hp = Math.Max(0, Math.Min(max, mon.Hp));
            float bar = mon.Bar >= 0f ? Mathf.Min(max, mon.Bar) : hp;
            int px = BarPixels(bar, max, BarW);
            if (px > 0) Fill(barX, at.y + BarY, px, BarH, bar * 5f <= max ? HpRed : bar * 2f <= max ? HpYellow : HpGreen);

            // 숫자 칸: 왼쪽 전투 피로 n/5, 오른쪽 남은/최대
            var inv = CultureInfo.InvariantCulture;
            int fatigue = Math.Max(0, Math.Min(FatigueRules.Max, mon.Fatigue));
            var fatigueInk = fatigue >= FatigueRules.Max ? HpRed : HpYellow;
            NumberLeft(fatigue.ToString(inv) + "/" + FatigueRules.Max.ToString(inv), nameX, at.y + NumY, fatigueInk, BoxShadow);
            Number(hp.ToString(inv) + "/" + max.ToString(inv), right, at.y + NumY, White, BoxShadow);
            if (!enemy)
            {
                // 내 쪽 EXP 막대 자리 = 전투 피로 다섯 칸(04 §4-2)
                Fill(at.x + FatigueLineX, at.y + FatigueLineY, 96, 1, BoxShadow);
                for (int i = 0; i < FatigueRules.Max; i++)
                    Fill(at.x + FatigueLineX + i * (FatigueSeg + FatigueGap), at.y + FatigueLineY, FatigueSeg, 1, i < fatigue ? fatigueInk : FatigueOff);
            }
        }

        // 픽셀 그림 한 장(줄마다 같은 글자가 이어진 만큼 한 번에 칠한다)
        void Glyph(string[] art, int x, int y)
        {
            for (int r = 0; r < art.Length; r++)
                for (int c = 0; c < art[r].Length;)
                {
                    char ch = art[r][c];
                    int n = 1;
                    while (c + n < art[r].Length && art[r][c + n] == ch) n++;
                    if (ch != '.') Fill(x + c, y + r, n, 1, ch == '#' ? BoxShadow : ch == 's' ? LockRing : ch == 'o' ? LockBody : ch == 'w' ? OrbLight : ch == 'd' ? OrbDark : OrbRed);
                    c += n;
                }
        }

        // 상자 바깥의 상태 배지 n번째(0부터)
        void Outside(Texture2D badge, Vector2Int at, bool enemy, int n)
        {
            var from = enemy ? EnemyBadgeOut : PlayerBadgeOut;
            Whole(badge, at.x + from.x + (enemy ? n * (badge.width + 1) : 0), at.y + from.y - (enemy ? 0 : n * (badge.height + 1)));
        }

        /// <summary>HP 막대 길이(px): 남은 체력 비율을 내림하되 체력이 남았으면 1칸은 남긴다.</summary>
        public static int BarPixels(int hp, int max, int width)
        {
            if (max <= 0 || hp <= 0) return 0;
            if (hp >= max) return width;
            return Math.Max(1, hp * width / max);
        }

        /// <summary>깎이는 중의 막대 길이(체력이 정수가 아닌 순간): 같은 규칙 — 내림, 남았으면 1칸.</summary>
        public static int BarPixels(float hp, int max, int width)
        {
            if (max <= 0 || hp <= 0f) return 0;
            if (hp >= max) return width;
            return Math.Max(1, Mathf.FloorToInt(hp * width / max));
        }

        /// <summary>체력 상자의 이름 칸 폭(px): 이름 시작에서 스피드 표시(⌃⌃ + 숫자) 앞까지.</summary>
        public static int NameWidth(bool enemy, int speed) => (enemy ? EnemyRight - EnemyNameX : PlayerRight - PlayerNameX) - (8 + Digits(speed) * DigitW) - 2;

        /// <summary>말줄임표(원작 자산 글꼴에 있는 글자).</summary>
        public const string Ellipsis = "…";

        /// <summary>
        /// 한 줄을 폭에 맞춘다: 넘치면 뒤를 덜어 내고 「…」를 붙인다(끝에 빈칸·여는 괄호를 남기지 않는다 — 「깝질무 (동쪽바다 폼)」 → 「깝질무 (동쪽…」).
        /// 넘치지 않으면 그대로. 전체 이름은 기둥·메시지 창에 나온다.
        /// </summary>
        public static string Fit(DsFont font, string text, int width)
        {
            if (font == null) throw new ArgumentNullException(nameof(font));
            if (string.IsNullOrEmpty(text) || font.Width(text) <= width) return text;
            for (int n = text.Length - 1; n > 0; n--)
            {
                string cut = text.Substring(0, n).TrimEnd(' ', '(') + Ellipsis;
                if (font.Width(cut) <= width) return cut;
            }
            return Ellipsis;
        }

        static int Digits(int n) => Math.Abs(n).ToString(CultureInfo.InvariantCulture).Length + (n < 0 ? 1 : 0);

        // 메시지 창: 원작 자산 창틀 9-slice(왼쪽 16 · 오른쪽 24 · 위아래 8) + 글 두 줄까지(넘치는 낱말은 다음 줄로)
        void Message(string text)
        {
            const int l = 16, r = 24, t = 8, b = 8;
            var box = messageBox;
            var m = MessageRect;
            int cw = box.width - l - r, ch = box.height - t - b, mw = m.width - l - r, mh = m.height - t - b;
            Piece(box, 0, 0, l, t, m.x, m.y, l, t);
            Piece(box, l, 0, cw, t, m.x + l, m.y, mw, t);
            Piece(box, l + cw, 0, r, t, m.x + l + mw, m.y, r, t);
            Piece(box, 0, t, l, ch, m.x, m.y + t, l, mh);
            Piece(box, l, t, cw, ch, m.x + l, m.y + t, mw, mh);
            Piece(box, l + cw, t, r, ch, m.x + l + mw, m.y + t, r, mh);
            Piece(box, 0, t + ch, l, b, m.x, m.y + t + mh, l, b);
            Piece(box, l, t + ch, cw, b, m.x + l, m.y + t + mh, mw, b);
            Piece(box, l + cw, t + ch, r, b, m.x + l + mw, m.y + t + mh, r, b);
            if (string.IsNullOrEmpty(text)) return;
            var lines = Wrap(messageFont, text, MessageTextRight - MessageTextX);
            if (lines.Count > 2) Cut.Add("메시지가 두 줄을 넘는다 「" + text + "」");
            foreach (var one in lines) if (messageFont.Width(one) > MessageTextRight - MessageTextX) Cut.Add("메시지 한 줄이 창 폭을 넘는다 「" + one + "」");
            for (int i = 0; i < lines.Count && i < 2; i++)
                Text(messageFont, lines[i], MessageTextX, MessageTextY + i * MessageLine, MessageTextRight, MsgInk, MsgShadow);
        }

        /// <summary>글을 폭에 맞춰 줄로 나눈다(빈칸에서 끊는다. 「\n」은 줄 바꿈). 낱말 하나가 폭을 넘으면 그 낱말은 한 줄에 그대로 둔다(잘려 찍힌다).</summary>
        public static List<string> Wrap(DsFont font, string text, int width)
        {
            var lines = new List<string>();
            foreach (var para in text.Split('\n'))
            {
                string line = "";
                foreach (var word in para.Split(' '))
                {
                    string next = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && font.Width(next) > width)
                    {
                        lines.Add(line);
                        line = word;
                    }
                    else line = next;
                }
                lines.Add(line);
            }
            return lines;
        }

        // ── 찍기 ──

        // 글 한 줄: (x, y)가 첫 칸의 왼쪽 위. maxRight를 넘는 글자는 찍지 않는다. 돌려주는 값 = 마지막 글자 뒤 x
        /// <summary>이번 장면에서 이름을 좁혀 찍었나(확인용 — 결정 87 V22).</summary>
        public bool NarrowShown { get; private set; }

        // 글 전체가 width 안에 들게 글자마다 가로만 줄여 찍는다(세로는 그대로 — 결정 87 V22 나). 돌려주는 값 = 글의 오른쪽 끝
        int TextNarrow(DsFont font, string text, int x, int y, int width, Color32 ink, Color32 shadow)
        {
            int full = font.Width(text);
            int sum = 0, start = x;
            foreach (char c in text)
            {
                var g = font.Of(c);
                int from = start + sum * width / full;
                sum += g.Advance;
                int to = start + sum * width / full;
                Stamp(font.Atlas, g.X, g.Y, g.Advance, font.Cell, from, y, to - from, font.Cell, 1, ink, shadow);
                x = to;
            }
            return x;
        }

        int Text(DsFont font, string text, int x, int y, int maxRight, Color32 ink, Color32 shadow)
        {
            if (string.IsNullOrEmpty(text)) return x;
            foreach (char c in text)
            {
                var g = font.Of(c);
                if (x + g.Advance > maxRight) break;
                Stamp(font.Atlas, g.X, g.Y, font.Cell, font.Cell, x, y, font.Cell, font.Cell, 1, ink, shadow);
                x += g.Advance;
            }
            return x;
        }

        // HP 숫자 그림(8×8 칸: 0~9 · 「/」)으로 찍는다. right = 오른쪽 끝
        void Number(string text, int right, int y, Color32 ink, Color32 shadow) => NumberLeft(text, right - text.Length * DigitW, y, ink, shadow);

        void NumberLeft(string text, int x, int y, Color32 ink, Color32 shadow)
        {
            foreach (char c in text)
            {
                int tile = c == '/' ? 10 : c >= '0' && c <= '9' ? c - '0' : -1;
                if (tile >= 0) Stamp(digits, tile * DigitW, 0, DigitW, 8, x, y, DigitW, 8, 2, ink, shadow);
                x += DigitW;
            }
        }

        void Whole(Texture2D tex, int x, int y)
        {
            if (tex != null) Piece(tex, 0, 0, tex.width, tex.height, x, y, tex.width, tex.height);
        }

        void Piece(Texture2D tex, int sx, int sy, int sw, int sh, int x, int y, int w, int h) => Stamp(tex, sx, sy, sw, sh, x, y, w, h, 0, White, White);

        void Fill(int x, int y, int w, int h, Color32 color) => Stamp(null, 0, 0, 1, 1, x, y, w, h, 3, color, color);

        void Stamp(Texture2D tex, int sx, int sy, int sw, int sh, int x, int y, int w, int h, int mode, Color32 ink, Color32 shadow)
        {
            if (mode != 3 && tex == null) return;
            var t = tex != null ? (Texture)tex : Texture2D.whiteTexture;
            block.Clear();
            block.SetTexture(TexId, t);
            block.SetVector(RectId, new Vector4(x, y, w, h));
            block.SetVector(SrcId, new Vector4(sx / (float)t.width, sy / (float)t.height, sw / (float)t.width, sh / (float)t.height));
            block.SetVector(InkId, ((Color)ink).linear);
            block.SetVector(ShadowId, ((Color)shadow).linear);
            block.SetFloat(ModeId, mode);
            cmd.DrawMesh(quad, Matrix4x4.identity, material, 0, 0, block);
        }
    }
}
