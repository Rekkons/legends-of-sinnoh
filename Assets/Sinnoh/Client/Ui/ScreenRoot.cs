using Rekkon.UiKit;
using Sinnoh.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sinnoh.Client
{
    /// <summary>
    /// 실행 중 화면 뿌리(04 §9, plan §4 「화면 뿌리·타이틀」): PC 층 패널 하나(<c>PanelRenderer</c> + 실행 중 만든 <c>PanelSettings</c>)를 쥐고 <see cref="UiStage"/>를 돌린다.
    /// <list type="bullet">
    /// <item>테마: 플레이어에는 <c>PanelSettings.Reset</c>이 없어 실행 중 만든 것의 테마가 null이다 — 씬(<see cref="ScreenBoot"/>)이 직렬화로 가리키는 실행용 테마를 받아 건다.</item>
    /// <item>해상도 등급(04 §9-3, 결정 44 ①): 창 안쪽 크기 → <see cref="UiGrade.For"/>(기준 크기가 다 들어가는 가장 큰 등급) → 토큰 벌 + 패널 정수 배율. 창 크기가 바뀌면 다시 고른다.
    /// 패널은 창 전체를 덮으므로 남는 자리는 화면 요소가 받는다.</item>
    /// <item>트윈 실행기(화면에 하나, 04 §8-6): 프레임마다 <c>Tweens.Advance</c>를 여기서만 부른다. 바쁨이 0이면 유휴 — 프레임을 <see cref="IdleFps"/>로 내린다(02 §9).</item>
    /// </list>
    /// </summary>
    public sealed class ScreenRoot : MonoBehaviour
    {
        public const int IdleFps = 30;

        public UiStage Stage { get; private set; }

        /// <summary>지금 유휴로 프레임을 내려 두었나.</summary>
        public bool Idle { get; private set; }

        /// <summary>패널에 걸린 테마(없으면 null — 뿌리는 그래도 패널을 덮는다, <see cref="UiStage.Bind"/>).</summary>
        public ThemeStyleSheet Theme => settings != null ? settings.themeStyleSheet : null;

        /// <summary>등급을 고른 크기(물리 px): 창 안쪽, 또는 <see cref="SetTarget"/>으로 준 RT.</summary>
        public Vector2Int Size => new Vector2Int(width, height);

        PanelSettings settings;
        KitSkin skin;
        RenderTexture target;
        int width, height;
        double carryMs;
        bool paced;
        int vSyncWas, fpsWas;

        /// <summary>겉모양(Gen 글꼴·창틀)을 읽어 화면 뿌리를 만든다. Gen 본체가 없으면 null. strings = 화면 문자열 표(<see cref="UiStage.Strings"/>).</summary>
        public static ScreenRoot Create(ThemeStyleSheet theme, StringTable strings = null)
        {
            var skin = UiSkin.Load();
            if (skin == null) return null;
            var root = new GameObject("ScreenRoot").AddComponent<ScreenRoot>();
            root.Init(skin, theme, strings);
            return root;
        }

        void Init(KitSkin kitSkin, ThemeStyleSheet theme, StringTable strings)
        {
            skin = kitSkin;
            Stage = new UiStage(skin, strings);
            Measure(out width, out height);
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "ScreenRoot Panel";
            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = UiGrade.For(width, height).PanelScale;
            var panel = gameObject.AddComponent<PanelRenderer>();
            panel.panelSettings = settings;
            panel.RegisterUIReloadCallback((PanelRenderer.VersionedUIReloadCallback)((r, root, version) => Stage.Bind(root, UiGrade.For(width, height))));
            vSyncWas = QualitySettings.vSyncCount;
            fpsWas = Application.targetFrameRate;
        }

        /// <summary>
        /// 패널을 화면 대신 RT에 그린다(투명 바탕) — 창 없는 확인용(<see cref="ScreenProbe"/>·PlayMode 시험). 등급은 RT 크기로 고른다. null이면 화면으로 되돌린다.
        /// 등급은 여기서 고르지 않는다 — 창 크기가 바뀔 때와 같은 길(다음 <c>Update</c>가 크기를 견준다)로 고른다.
        /// </summary>
        public void SetTarget(RenderTexture rt)
        {
            target = rt;
            settings.targetTexture = rt;
            settings.clearColor = rt != null;
            settings.colorClearValue = Color.clear;
        }

        void Measure(out int w, out int h)
        {
            if (target != null) { w = target.width; h = target.height; }
            else { w = Screen.width; h = Screen.height; }
        }

        void Regrade()
        {
            Measure(out int w, out int h);
            if (w == width && h == height) return;
            width = w;
            height = h;
            var grade = UiGrade.For(w, h);
            settings.scale = grade.PanelScale;
            Stage.SetGrade(grade);
        }

        void Update()
        {
            if (Stage == null) return;
            Regrade();
            carryMs += Time.unscaledDeltaTime * 1000.0;
            int dt = (int)carryMs;
            carryMs -= dt;
            Stage.Tweens.Advance(dt);
            bool idle = Stage.Tweens.Busy == 0;
            Meter(idle, Time.unscaledDeltaTime * 1000.0);
            Pace(idle);
        }

        /// <summary>
        /// 프레임 재기(plan M7 완료 조건 6 「웹 손님에서 연출 중 프레임이 유휴값으로 떨어지지 않는다」): 바쁜 구간 · 유휴 구간이 끝날 때 그 구간의 평균 fps를 쥔다(<see cref="MeterMinMs"/> 이상인 구간만).
        /// 웹 손님판은 로그 한 줄(<c>[Frame] busy 2412ms 144f 59.7fps</c>)도 남긴다 — 헤드리스 브라우저 확인이 콘솔에서 읽는다.
        /// </summary>
        public float LastBusyFps { get; private set; }
        public float LastIdleFps { get; private set; }
        /// <summary>지금까지 잰 바쁜 구간 가운데 가장 낮은 fps(없으면 0).</summary>
        public float MinBusyFps { get; private set; }
        public const double MeterMinMs = 1000;

        bool meterIdle = true;
        int meterFrames;
        double meterMs;

        void Meter(bool idle, double frameMs)
        {
            if (idle != meterIdle)
            {
                if (meterMs >= MeterMinMs && meterFrames > 0)
                {
                    float fps = (float)(meterFrames * 1000.0 / meterMs);
                    if (meterIdle) LastIdleFps = fps;
                    else
                    {
                        LastBusyFps = fps;
                        if (MinBusyFps <= 0 || fps < MinBusyFps) MinBusyFps = fps;
                    }
                    if (Application.platform == RuntimePlatform.WebGLPlayer)
                        Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[Frame] {0} {1:0}ms {2}f {3:0.0}fps", meterIdle ? "idle" : "busy", meterMs, meterFrames, fps));
                }
                meterIdle = idle;
                meterFrames = 0;
                meterMs = 0;
                meterNoted = false;
            }
            meterFrames++;
            meterMs += frameMs;
            // 웹: 긴 구간은 5초에 닿을 때도 한 줄(끝나지 않는 유휴 구간 — 연출 없는 화면 — 도 읽히게)
            if (!meterNoted && meterMs >= MeterNoteMs && Application.platform == RuntimePlatform.WebGLPlayer)
            {
                meterNoted = true;
                Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "[Frame] {0} {1:0}ms+ {2}f {3:0.0}fps", meterIdle ? "idle" : "busy", meterMs, meterFrames, meterFrames * 1000.0 / meterMs));
            }
        }

        const double MeterNoteMs = 5000;
        bool meterNoted;

        // 유휴면 수직 동기를 끄고 프레임 제한을 건다(수직 동기가 켜져 있으면 targetFrameRate가 듣지 않는다). 바쁘면 처음 값으로 되돌린다.
        void Pace(bool idle)
        {
            if (paced && idle == Idle) return;
            paced = true;
            Idle = idle;
            QualitySettings.vSyncCount = idle ? 0 : vSyncWas;
            Application.targetFrameRate = idle ? IdleFps : fpsWas;
        }

        void OnDestroy()
        {
            if (Stage == null) return;
            if (paced)
            {
                QualitySettings.vSyncCount = vSyncWas;
                Application.targetFrameRate = fpsWas;
            }
            Stage.Shut();
            if (settings != null) Destroy(settings);
            UiSkin.Release(skin);
        }
    }
}
