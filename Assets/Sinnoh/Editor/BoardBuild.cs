using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Sinnoh.Client;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Sinnoh.Editor
{
    // 보드 플레이어 빌드(M4-2e, d3-3 §2-4·§2-5·§4-2~§4-4): Gen 번들 + Win64 플레이어. 배치로만 돌린다(창을 띄우지 않는다).
    //   Unity.exe -batchmode -projectPath . -executeMethod Sinnoh.Editor.BoardBuild.Win64 -logFile <log>   (-quit 없이, 끝나면 종료 코드 0/1)
    // 번들(03 §5 「나눠 받기」를 M4-2 측정용으로 먼저, d3-3 §4-2 [가정]): 키 → 번들 이름은 GenAssets.BundleFor 한 곳(board·trainers·figures·fig_<폴더>·ui·sprites·cards·stage·audio) + 피규어 URP 셰이더 공용 fig_shaders.
    //   LZ4(ChunkBasedCompression), 주소 이름 = Gen 키. Builds/bundles/<대상>/content/에 만들고 플레이어 출력의 StreamingAssets/content/에 복사한다(.manifest 글은 뺀다).
    // 출력 Builds/(git 밖): Win64/Sinnoh.exe(+ Sinnoh_Data/StreamingAssets/content/).
    //   끝에 Tools/bin/cloudflared.exe(node Tools/fetch-cloudflared.mjs — 결정 91)를 실행 파일 옆으로 복사한다(없으면 경고만 — 인터넷 방의 Relay 실패 때 터널이 없다).
    //   끝에 LICENSE.md · LICENSE.ko.md · THIRD-PARTY-NOTICES.md · THIRD-PARTY-NOTICES.ko.md · CREDITS.md · CREDITS.ko.md · PRIVACY.md · PRIVACY.ko.md를 Sinnoh.exe 옆에 복사한다(CopyNotices).
    // 설정(03 §5·d3-3 §2-4·§2-5): Frame Timing Stats 켬, 스플래시 끔, 게임 아이콘(ApplyIcons — 결정 159 ①) — 프로젝트 설정에 남는다.
    //   아이콘만 넣기: Unity.exe -batchmode -projectPath . -executeMethod Sinnoh.Editor.BoardBuild.Icons -logFile <log>
    // 결과 한 줄 [BoardBuild] + Logs/BoardShots/build-win64.json(시간·크기·설정·번들 크기).
    public static class BoardBuild
    {
        public const string OutRoot = "Builds";
        public const string ExeName = "Sinnoh";
        /// <summary>실행 파일 옆 판 번호 파일(<c>Application.version</c> 한 줄 — Tools/release/pack.mjs VERSION_FILE과 같은 이름).</summary>
        public const string VersionFile = "version.txt";
        const string ReportDir = "Logs/BoardShots";

        [MenuItem("Sinnoh/Build/Win64 Board")]
        static void MenuWin64() => BuildWin64();

        public static void Win64() => Batch(BuildWin64);

        public static void Icons() => Batch(() => { ApplyIcons(); SavePlayerSettings(); return true; });

        static void Batch(Func<bool> f)
        {
            bool ok = false;
            try { ok = f(); }
            catch (Exception e) { Debug.LogException(e); }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// 플레이어 번들 목록: 보드가 읽는 Gen 키(Board/**, Trainers/trainers.json·Front/**, Figures/figures.json·폴더마다 프리팹)와 PC 층 겉모양(UiSkin의 글꼴·창틀 키 셋 → ui), 셋업 화면 그림(정지 도트 아틀라스 → sprites, 마을 카드 그림·체력·스피드 아이콘 → cards)을 GenAssets.BundleFor로 묶는다.
        /// 피규어 재질의 셰이더는 fig_shaders 하나로 뺀다(피규어마다 겹치지 않게, FigureImport 크기 재기와 같다). 디스크 순서가 아니라 서수 정렬(결정적).
        /// </summary>
        public static List<AssetBundleBuild> Plan()
        {
            var byBundle = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            void Add(string key)
            {
                string b = GenAssets.BundleFor(key) ?? throw new InvalidOperationException("[BoardBuild] 번들 없는 키 " + key);
                if (!byBundle.TryGetValue(b, out var set)) byBundle[b] = set = new SortedSet<string>(StringComparer.Ordinal);
                set.Add(key);
            }
            foreach (var k in Keys("Board", true)) Add(k);
            foreach (var k in Keys("Trainers", false).Where(k => k == "Trainers/trainers.json")) Add(k);
            foreach (var k in Keys("Trainers/Front", true)) Add(k);
            // 포획 연출(04 §8-8 68행): 주인공 공식 뒷모습 「볼 던지기」 시트
            foreach (var k in Keys("Trainers/Back/official", true)) Add(k);
            // 얼굴 상자(04 §3-6, 결정 54 ④): 주인공 앞모습 8
            foreach (var k in Keys("Trainers/Hero", true)) Add(k);
            // 주인공 도트 말·주인공 고르기(04 §3-1): 필드 도트 표 + 시트 8
            foreach (var k in Keys("Field", true)) Add(k);
            foreach (var k in Keys("Figures", false).Where(k => k == "Figures/figures.json")) Add(k);
            foreach (var k in new[] { UiSkin.FontKey, UiSkin.PanelFrameKey, UiSkin.TipFrameKey })
                if (File.Exists(GenAssets.Root + k)) Add(k);
            // 셋업 화면(S12·S13): 포켓몬 정지 도트 아틀라스, 마을 카드 그림, 체력·스피드 아이콘
            foreach (var k in Keys("Sprites/Static", true)) Add(k);
            foreach (var k in Keys("Cards/Village", true)) Add(k);
            foreach (var k in Keys("Cards/Generator/stats_icons", true)) Add(k);
            foreach (var k in Keys("Cards/Trainer", false).Where(k => GenAssets.BundleFor(k) == "cards")) Add(k);   // 트레이너 카드 세로 뒷면 부품 둘 + 해금 연출의 카드 틀 13(M6)
            foreach (var k in Keys("Cards/Fate", true)) Add(k);   // 운명의 변화 카드 앞면 넷 + 뒷면(M5 「운명·응답 화면」)
            foreach (var k in Keys("Cards/Shrine", true)) Add(k);   // 사당 카드 그림 — 전설 8 + 축복 1(결정 80 L12 나)
            foreach (var k in Keys("Cards/Act", true).Where(k => k == GenAssets.EndingArt)) Add(k);   // 종막 문양(결정 80 L1 나)
            foreach (var k in Keys("Cards/Trainer/icons", true)) Add(k);   // 트레이너 카드 전용 아이템 아이콘 11(rulings G1 — M6 그림 에셋)
            foreach (var k in Keys("Cards/Breach", true)) Add(k);   // 왜곡 균열 카드 그림 6(M6 그림 에셋)
            foreach (var k in Keys("Cards/Onslaught", true)) Add(k);   // 울트라 온슬롯 카드 그림 1(결정 87 V24 나)
            foreach (var k in Keys("Cards/Stamp", true)) Add(k);   // 여행 스탬프 시트 · 도장(M9 다듬기 P12 — 결정 80 L18 나)
            foreach (var k in Keys("Cards/Tokens/celestial_icons", false).Where(k => GenAssets.BundleFor(k) == "cards")) Add(k);   // 추종자 아이콘 셋(M5 「사당·균열 화면」)
            // 조우·포획 화면(S45·S50): 조우 등급 그림, 볼·규토리 그림
            foreach (var k in Keys("Cards/Generator/encounter_icons", true)) Add(k);
            foreach (var k in Keys("Cards/Tokens/item_icons", false).Where(GenAssets.IsItemIcon)) Add(k);
            foreach (var k in Keys("Cards/Tokens/item_tokens", false).Where(k => GenAssets.BundleFor(k) == "cards")) Add(k);   // 유물·우두머리 토큰 앞면 12(M5 「맵 결과·배틀 안 연출」) + 금색 ? 뒷면 1(결정 87 V8 나)
            // 배틀 화면의 DS 층: 무대(배경·발판·체력 상자·창틀·타입·상태 그림 + 지형 짝 표)와 원작 자산 글꼴(표 + 아틀라스). ui 번들의 창틀 둘은 거기 그대로
            foreach (var k in Keys("Stage", true).Where(k => GenAssets.BundleFor(k) == "stage")) Add(k);
            foreach (var k in Keys("Fonts", false).Where(k => GenAssets.BundleFor(k) == "stage")) Add(k);
            // 소리(결정 66 ④): 상황 곡 표 + 그 표가 가리키는 곡(Pt 원곡 — 반복 클립 · 앞부분 클립) + 효과음 표·클립
            if (File.Exists(GenAssets.Root + SoundPlayer.MusicTableKey))
            {
                Add(SoundPlayer.MusicTableKey);
                var book = Sinnoh.Fx.MusicBook.Parse(File.ReadAllText(GenAssets.Root + SoundPlayer.MusicTableKey));
                foreach (Sinnoh.Fx.MusicScene scene in Enum.GetValues(typeof(Sinnoh.Fx.MusicScene)))
                {
                    if (book.File(scene) != null) Add(SoundPlayer.MusicKey(book.File(scene)));
                    if (book.Intro(scene) != null) Add(SoundPlayer.MusicKey(book.Intro(scene)));   // 앞부분 클립(M9 소리 ② — 같은 번들)
                }
            }
            foreach (var k in Keys("Audio/Se", true)) Add(k);
            foreach (var k in Keys("Audio/Cry", true)) Add(k);   // 울음 표 + 울음(M9 소리 ③ — 번들 cries)
            // 튜토리얼 안내 마박사(M6.5, 결정 90 Q2 가): 소개 전신 도트 + 목차 얼굴
            foreach (var k in Keys("Tutorial", true)) Add(k);
            // 설명서 그림(M9 「설명서 그림」, 결정 35): 원작 룰북에서 자른 그림 + 표 — 번들 rulebook
            foreach (var k in Keys("Rulebook", true)) Add(k);
            // 포켓몬 카드(결정 206): 구운 앞면 · 띠 · 숫자 글자 + 카드에 얹는 상태 주사위 · 상자 · 타입 그림 · 지닌물건 토큰 — pokecards · pokecards_*
            foreach (var dir in new[] { "Cards/Faces", "Cards/Moves", "Cards/Strips", "Cards/Digits", "Cards/Dice", "Cards/Generator/stats_bases", "Cards/Generator/types", "Cards/Tokens/item_tokens" })
                foreach (var k in Keys(dir, true).Where(k => IsCardBundle(GenAssets.BundleFor(k)))) Add(k);
            var shaders = new SortedSet<string>(StringComparer.Ordinal);
            string figRoot = GenAssets.Root + "Figures";
            if (Directory.Exists(figRoot))
                foreach (var dir in Directory.GetDirectories(figRoot).Select(d => d.Replace('\\', '/')).OrderBy(d => d, StringComparer.Ordinal))
                {
                    string sub = "Figures/" + Path.GetFileName(dir);
                    foreach (var k in Keys(sub, false).Where(k => k.EndsWith(".prefab", StringComparison.Ordinal))) Add(k);
                    foreach (var k in Keys(sub, true).Where(k => k.EndsWith(".mat", StringComparison.Ordinal)))
                    {
                        var m = AssetDatabase.LoadAssetAtPath<Material>(GenAssets.Root + k);
                        string sp = m != null && m.shader != null ? AssetDatabase.GetAssetPath(m.shader) : null;
                        if (!string.IsNullOrEmpty(sp)) shaders.Add(sp);
                    }
                }
            var builds = new List<AssetBundleBuild>();
            foreach (var kv in byBundle)
            {
                var keys = kv.Value.ToArray();
                builds.Add(new AssetBundleBuild { assetBundleName = kv.Key, assetNames = keys.Select(k => GenAssets.Root + k).ToArray(), addressableNames = keys });
            }
            if (shaders.Count > 0)
                builds.Add(new AssetBundleBuild { assetBundleName = GenAssets.FigureShadersBundle, assetNames = shaders.ToArray(), addressableNames = shaders.ToArray() });
            return builds;
        }

        /// <summary>Gen/&lt;dir&gt; 아래 본체 파일의 키(.meta 뺌).</summary>
        static IEnumerable<string> Keys(string dir, bool recursive)
        {
            string root = GenAssets.Root + dir;
            if (!Directory.Exists(root)) return Enumerable.Empty<string>();
            return Directory.GetFiles(root, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => !f.EndsWith(".meta", StringComparison.Ordinal))
                .Select(f => f.Replace('\\', '/').Substring(GenAssets.Root.Length))
                .OrderBy(k => k, StringComparer.Ordinal);
        }

        /// <summary>게임 아이콘(결정 159 ① — 직접 그린 도트): 16 · 32가 원본, 나머지는 정수배 최근접(48 = 16 ×3, 64~1024 = 32 ×2~32).</summary>
        public const string IconDir = "Assets/Sinnoh/Art/Authored/Icon/";
        public static string IconPath(int size) => IconDir + "icon-" + size + ".png";

        /// <summary>
        /// 기본 아이콘(256)과 Standalone 크기별 아이콘(실행 파일 · 창 · 작업 표시줄)을 프로젝트 설정에 넣는다 — 크기마다 같은 크기의 그림(Unity가 늘리거나 줄이지 않게).
        /// 그림은 Point · 밉맵 없음 · 무압축으로 들여온다(다르면 고쳐 다시 들여온다). 크기에 맞는 그림이 없으면 예외(빌드 실패).
        /// </summary>
        public static void ApplyIcons()
        {
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { LoadIcon(256) }, IconKind.Any);
            var sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any);
            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, sizes.Select(LoadIcon).ToArray(), IconKind.Any);
        }

        static Texture2D LoadIcon(int size)
        {
            string path = IconPath(size);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter ?? throw new FileNotFoundException("[BoardBuild] 아이콘 그림 없음 " + path, path);
            if (ti.filterMode != FilterMode.Point || ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed || ti.npotScale != TextureImporterNPOTScale.None)
            {
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex.width != size || tex.height != size) throw new InvalidOperationException("[BoardBuild] 아이콘 크기 " + tex.width + "×" + tex.height + " ≠ " + size + " — " + path);
            return tex;
        }

        /// <summary>
        /// UI Toolkit 필터의 기본 셰이더(<see cref="ScreenProbe.FilterShaders"/>)를 그래픽 설정 Always Included Shaders에 넣는다(없는 것만 — 커밋된 설정에 이미 있다).
        /// 플레이어 빌드에서 빠지면 USS <c>filter: drop-shadow</c> 요소(카드 블럭 그림자)를 그릴 때마다 예외가 난다. 셰이더를 찾지 못하면 예외(빌드 실패).
        /// </summary>
        public static void EnsureFilterShaders()
        {
            var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (string name in ScreenProbe.FilterShaders)
            {
                var sh = Shader.Find(name) ?? throw new InvalidOperationException("[BoardBuild] UI 필터 셰이더 없음 " + name);
                bool has = false;
                for (int i = 0; i < list.arraySize; i++) has |= list.GetArrayElementAtIndex(i).objectReferenceValue == sh;
                if (has) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = sh;
                Debug.Log("[BoardBuild] Always Included Shaders += " + name);
            }
            if (so.ApplyModifiedPropertiesWithoutUndo()) AssetDatabase.SaveAssets();
        }

        public static void FilterShaders() => Batch(() => { EnsureFilterShaders(); return true; });

        public static bool BuildWin64()
        {
            PlayerSettings.enableFrameTimingStats = true; // d3-3 §2-4
            PlayerSettings.SplashScreen.show = false;     // 03 §5
            EnsureFilterShaders();                        // 카드 블럭 그림자(결정 206)
            ApplyIcons();                                 // 결정 159 ①
            SavePlayerSettings();
            bool ok = Build(BuildTarget.StandaloneWindows64, "win64", Path.Combine(OutRoot, "Win64", ExeName + ".exe"), Path.Combine(OutRoot, "Win64", ExeName + "_Data", "StreamingAssets"));
            if (ok)
            {
                string exeDir = Path.Combine(OutRoot, "Win64");
                CopyHostExtras(exeDir);
                CopyNotices(".", exeDir);
                // 판 번호(결정 148 — 정본 bundleVersion): 포장 도구 Tools/release/pack.mjs가 읽는다(빌드한 판 = 포장할 판)
                File.WriteAllText(Path.Combine(exeDir, VersionFile), Application.version + "\n");
            }
            return ok;
        }

        /// <summary>배포본에 함께 넣는 문서(plan §5 #534 — Apache-2.0 · OFL · MIT는 라이선스 사본을 함께 줄 것을 요구한다).</summary>
        public static readonly string[] NoticeFiles = { "LICENSE.md", "LICENSE.ko.md", "THIRD-PARTY-NOTICES.md", "THIRD-PARTY-NOTICES.ko.md", "CREDITS.md", "CREDITS.ko.md", "PRIVACY.md", "PRIVACY.ko.md" };   // PRIVACY = 개인 정보 처리 고지(결정 126 ④ — 플레이 기록 보내기). LICENSE · THIRD-PARTY-NOTICES · CREDITS · PRIVACY는 영어 *.md + 한국어 *.ko.md(결정 138 · 152)

        /// <summary>
        /// 라이선스 · 고지 · 크레딧 · 개인 정보 처리 문서(NoticeFiles)를 srcRoot(프로젝트 뿌리)에서 dstDir(실행 파일 옆)로 복사한다.
        /// 하나라도 없으면 예외(문서 없는 배포본을 만들지 않는다 — 빌드 실패).
        /// </summary>
        public static void CopyNotices(string srcRoot, string dstDir)
        {
            foreach (string name in NoticeFiles)
                if (!File.Exists(Path.Combine(srcRoot, name))) throw new FileNotFoundException("[BoardBuild] 배포 문서 없음 " + name, Path.Combine(srcRoot, name));
            Directory.CreateDirectory(dstDir);
            foreach (string name in NoticeFiles) File.Copy(Path.Combine(srcRoot, name), Path.Combine(dstDir, name), true);
            Debug.Log("[BoardBuild] 배포 문서 " + NoticeFiles.Length + "개 → " + dstDir);
        }

        /// <summary>
        /// 호스트 실행 파일 옆에 cloudflared.exe를 둔다(02 §2-1 — 인터넷 방의 Relay 실패 때 터널). 없으면 경고만.
        /// 같은 출력 폴더에 예전 빌드가 남긴 웹 손님판 폴더(<c>WebGL/</c> — 결정 210으로 뺐다)가 있으면 지운다(포장에 섞이지 않게).
        /// </summary>
        public static void CopyHostExtras(string exeDir)
        {
            string stale = Path.Combine(exeDir, "WebGL");
            if (Directory.Exists(stale))
            {
                Directory.Delete(stale, true);
                Debug.Log("[BoardBuild] 예전 웹 손님판 폴더를 지웠다 → " + stale);
            }
            string cf = Path.Combine("Tools", "bin", "cloudflared.exe");
            if (File.Exists(cf)) File.Copy(cf, Path.Combine(exeDir, "cloudflared.exe"), true);
            else Debug.LogWarning("[BoardBuild] Tools/bin/cloudflared.exe가 없다(node Tools/fetch-cloudflared.mjs) — 인터넷 방은 Relay가 실패하면 터널 없이 실패한다");
        }

        /// <summary>포켓몬 카드 번들(<see cref="GenAssets.CardBundle"/>와 그 나눈 묶음 <c>pokecards_*</c>)인가.</summary>
        public static bool IsCardBundle(string bundle) => bundle != null && (bundle == GenAssets.CardBundle || bundle.StartsWith(GenAssets.CardBundle + "_", StringComparison.Ordinal));

        /// <summary>
        /// 바꾼 Player Settings만 디스크에 쓴다(AssetDatabase.SaveAssets는 다른 더러운 설정 자산까지 쓴다).
        /// 플레이어 빌드 자체도 URP 설정 자산(DefaultVolumeProfile·URP 전역 설정·GraphicsSettings)을 다시 쓴다 — 커밋 전에 되돌린다(plan §4 M4-2e).
        /// </summary>
        static void SavePlayerSettings()
        {
            foreach (var ps in Resources.FindObjectsOfTypeAll<PlayerSettings>()) AssetDatabase.SaveAssetIfDirty(ps);
        }

        static bool Build(BuildTarget target, string tag, string location, string streamingAssets)
        {
            var t0 = DateTime.Now;
            var inv = CultureInfo.InvariantCulture;
            string bundleDir = Path.Combine(OutRoot, "bundles", target.ToString(), GenAssets.ContentFolder);
            if (Directory.Exists(bundleDir)) Directory.Delete(bundleDir, true);
            Directory.CreateDirectory(bundleDir);
            var plan = Plan();
            if (plan.Count == 0) { Debug.LogError("[BoardBuild] Gen 없음: rebuild-all 먼저"); return false; }
            var manifest = BuildPipeline.BuildAssetBundles(bundleDir, plan.ToArray(), BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
            if (manifest == null) { Debug.LogError("[BoardBuild] 번들 만들기 실패"); return false; }
            double bundleSec = (DateTime.Now - t0).TotalSeconds;

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = location, target = target, options = BuildOptions.None });
            bool ok = report.summary.result == BuildResult.Succeeded;

            var bundles = new List<(string name, long bytes)>();
            if (ok)
            {
                string dst = Path.Combine(streamingAssets, GenAssets.ContentFolder);
                if (Directory.Exists(dst)) Directory.Delete(dst, true);
                Directory.CreateDirectory(dst);
                foreach (var f in Directory.GetFiles(bundleDir).Where(f => !f.EndsWith(".manifest", StringComparison.Ordinal)).OrderBy(f => f, StringComparer.Ordinal))
                {
                    File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
                    bundles.Add((Path.GetFileName(f), new FileInfo(f).Length));
                }
            }

            double sec = (DateTime.Now - t0).TotalSeconds;
            var nt = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));
            var sb = new StringBuilder("{\"schema\":1");
            sb.Append(",\"target\":\"").Append(target).Append('"')
              .Append(",\"result\":\"").Append(report.summary.result).Append('"')
              .Append(",\"location\":\"").Append(location.Replace('\\', '/')).Append('"')
              .Append(",\"seconds\":").Append(sec.ToString("0.0", inv))
              .Append(",\"bundleSeconds\":").Append(bundleSec.ToString("0.0", inv))
              .Append(",\"playerBytes\":").Append(report.summary.totalSize)
              .Append(",\"errors\":").Append(report.summary.totalErrors)
              .Append(",\"scenes\":[").Append(string.Join(",", scenes.Select(s => "\"" + s + "\""))).Append(']')
              .Append(",\"settings\":{\"scriptingBackend\":\"").Append(PlayerSettings.GetScriptingBackend(nt)).Append('"')
              .Append(",\"managedStripping\":\"").Append(PlayerSettings.GetManagedStrippingLevel(nt)).Append('"')
              .Append(",\"stripEngineCode\":").Append(PlayerSettings.stripEngineCode ? "true" : "false")
              .Append(",\"il2cppCodeGeneration\":\"").Append(PlayerSettings.GetIl2CppCodeGeneration(nt)).Append('"')
              .Append(",\"frameTimingStats\":").Append(PlayerSettings.enableFrameTimingStats ? "true" : "false")
              .Append(",\"splash\":").Append(PlayerSettings.SplashScreen.show ? "true" : "false");
            sb.Append("},\"bundles\":[");
            for (int i = 0; i < bundles.Count; i++)
                sb.Append(i > 0 ? "," : "").Append("{\"name\":\"").Append(bundles[i].name).Append("\",\"bytes\":").Append(bundles[i].bytes).Append('}');
            sb.Append("]}");
            Directory.CreateDirectory(ReportDir);
            File.WriteAllText(Path.Combine(ReportDir, "build-" + tag + ".json"), sb.ToString(), new UTF8Encoding(false));

            string line = $"[BoardBuild] {target} {report.summary.result}, 플레이어 {report.summary.totalSize:N0}바이트, 번들 {bundles.Count}개 {bundles.Sum(b => b.bytes):N0}바이트, 오류 {report.summary.totalErrors}, {sec:0.0}초 → {location}";
            if (ok) Debug.Log(line); else Debug.LogError(line);
            return ok;
        }
    }
}
