using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

namespace Sinnoh.Client
{
    /// <summary>
    /// 웹 손님판의 Gen 번들 받기(M7 웹 손님 — 03 §5 「나눠 받기」, [가정 M7-63]): 페이지 주소의 <c>StreamingAssets/content/&lt;번들&gt;.gz</c>를 UnityWebRequestAssetBundle로 받는다
    /// (서버 — 호스트의 WebGuestServer · Tools/serve-web.mjs — 가 <c>Content-Encoding: gzip</c>을 붙여 브라우저가 푼다. 번들 자체는 LZ4).
    /// 1. 번들 목록(<c>content</c>) → 2. 첫 받기(<see cref="GenAssets.Split"/>가 아닌 것 전부 — 글꼴 · 창틀 · 보드 · 카드 · 무대 · 효과음 …, 동시 4개)를 다 받으면 <see cref="Ready"/>
    /// (보드 · 화면 시작이 기다린다 — <see cref="GenAssets.WhenReady"/>, 그동안 페이지의 받기 막대가 이어 찬다) → 3. 뒤에서 나눠 받기(셰이더 공용 → 피규어 → 곡, 동시 2개 — 받을 때마다 <see cref="GenAssets.Arrived"/>).
    /// 받지 못한 번들은 없는 것으로 친다(그 그림 · 소리 없이 돈다 — 경고 로그). 웹 플레이어에서만 켜진다(에디터 · Win64는 아무것도 하지 않는다).
    /// </summary>
    public sealed class WebContent : MonoBehaviour
    {
        /// <summary>전송 파일 꼬리(BoardBuild가 WebGL 번들마다 gzip으로 만든다).</summary>
        public const string Ext = ".gz";
        public const int FirstAtOnce = 4, SplitAtOnce = 2;

        static readonly Dictionary<string, AssetBundle> loaded = new Dictionary<string, AssetBundle>(StringComparer.Ordinal);
        static readonly HashSet<string> waiting = new HashSet<string>(StringComparer.Ordinal);
        static readonly List<Action> whenReady = new List<Action>();

        /// <summary>첫 받기를 다 받았다(실패해도 — 없는 것으로 친다).</summary>
        public static bool Ready { get; private set; }
        /// <summary>받은 번들 수 · 받을 번들 수(첫 받기 + 나눠 받기).</summary>
        public static int Done { get; private set; }
        public static int Total { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            loaded.Clear();
            waiting.Clear();
            whenReady.Clear();
            wanted = null;
            Ready = false;
            Done = Total = 0;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void SinnohLoading(int done, int total, int ready);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("WebContent");
            DontDestroyOnLoad(go);
            go.AddComponent<WebContent>().StartCoroutine(go.GetComponent<WebContent>().Run());
        }
#else
        static void SinnohLoading(int done, int total, int ready) { }
#endif

        /// <summary>받아 둔 번들(없으면 null).</summary>
        public static AssetBundle Loaded(string name) => name != null && loaded.TryGetValue(name, out var b) ? b : null;

        /// <summary>아직 받는 피규어 번들이 있나.</summary>
        public static bool PendingFigures
        {
            get
            {
                foreach (var n in waiting) if (n.StartsWith("fig_", StringComparison.Ordinal)) return true;
                return false;
            }
        }

        /// <summary>그 번들을 아직 받는 중인가.</summary>
        public static bool Pending(string name) => name != null && waiting.Contains(name);

        static string wanted;

        /// <summary>
        /// 지금 상황의 곡 번들(결정 112 Q18): 나눠 받기에서 아직 시작하지 않았으면 다음 차례로 당겨 받는다 — 첫 받기(40 MB 문턱)는 그대로 두고, 듣는 곡이 피규어 뒤로 밀리지 않게.
        /// 소리 재생 층이 곡이 비어 있는 상황을 열 때 부른다(<see cref="SoundPlayer"/>). 웹 밖에서는 아무 일도 없다.
        /// </summary>
        public static void Want(string bundle)
        {
            if (bundle != null && waiting.Contains(bundle)) wanted = bundle;
        }

        /// <summary>다음에 받을 번들 색인: 바라는 곡이 아직 시작 전이면 그것을 next 자리로 당긴다(순서 표를 바꾼다).</summary>
        public static int Pull(List<string> names, int next, string want)
        {
            if (want == null || names == null || next >= names.Count) return next;
            int at = names.IndexOf(want, next);
            if (at > next)
            {
                names.RemoveAt(at);
                names.Insert(next, want);
            }
            return next;
        }

        public static void WhenReady(Action action)
        {
            if (action == null) return;
            if (Ready) action();
            else whenReady.Add(action);
        }

        static string Url(string name) => Application.streamingAssetsPath + "/" + GenAssets.ContentFolder + "/" + name + Ext;

        IEnumerator Run()
        {
            AssetBundleManifest manifest = null;
            using (var req = UnityWebRequestAssetBundle.GetAssetBundle(Url(GenAssets.ContentFolder)))
            {
                yield return req.SendWebRequest();
                var mb = req.result == UnityWebRequest.Result.Success ? DownloadHandlerAssetBundle.GetContent(req) : null;
                if (mb != null) manifest = mb.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                if (manifest == null) Debug.LogWarning("[WebContent] 번들 목록을 받지 못했다: " + Url(GenAssets.ContentFolder) + " " + req.error);
            }
            var first = new List<string>();
            var split = new List<string>();
            if (manifest != null)
                foreach (var n in manifest.GetAllAssetBundles())
                    (GenAssets.Split(n) ? split : first).Add(n);
            // 나눠 받기 순서 [가정 M7-63]: 셰이더 공용(피규어가 기댄다) → 피규어(보드 전경) → 곡
            split.Sort((a, b) => Rank(a) != Rank(b) ? Rank(a).CompareTo(Rank(b)) : string.CompareOrdinal(a, b));
            foreach (var n in first) waiting.Add(n);
            foreach (var n in split) waiting.Add(n);
            Total = first.Count + split.Count;
            SinnohLoading(0, Total, 0);
            yield return Fetch(first, FirstAtOnce);
            Ready = true;
            SinnohLoading(Done, Total, 1);
            Debug.Log($"[WebContent] 첫 받기 {first.Count}개 끝 — 나눠 받기 {split.Count}개를 뒤에서");
            var calls = whenReady.ToArray();
            whenReady.Clear();
            foreach (var a in calls)
            {
                try { a(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (split.Count > 0 && split[0] == GenAssets.FigureShadersBundle)
            {
                yield return Fetch(split.GetRange(0, 1), 1);
                split.RemoveAt(0);
            }
            yield return Fetch(split, SplitAtOnce);
            Debug.Log($"[WebContent] 나눠 받기 끝 — 받은 번들 {loaded.Count}/{Total}");
        }

        static int Rank(string n) => n == GenAssets.FigureShadersBundle ? 0 : n.StartsWith("fig_", StringComparison.Ordinal) ? 1 : 2;

        IEnumerator Fetch(List<string> names, int atOnce)
        {
            int next = 0, running = 0;
            while (next < names.Count || running > 0)
            {
                while (running < atOnce && next < names.Count)
                {
                    next = Pull(names, next, wanted);
                    if (names[next] == wanted) wanted = null;
                    running++;
                    StartCoroutine(One(names[next++], () => running--));
                }
                yield return null;
            }
        }

        IEnumerator One(string name, Action done)
        {
            using (var req = UnityWebRequestAssetBundle.GetAssetBundle(Url(name)))
            {
                yield return req.SendWebRequest();
                AssetBundle b = null;
                if (req.result == UnityWebRequest.Result.Success) b = DownloadHandlerAssetBundle.GetContent(req);
                else Debug.LogWarning("[WebContent] 번들을 받지 못했다(없는 것으로 친다): " + name + " " + req.error);
                if (b != null) loaded[name] = b;
                waiting.Remove(name);
                Done++;
                SinnohLoading(Done, Total, Ready ? 1 : 0);
                if (Ready) GenAssets.NoteArrived(name);
            }
            done();
        }
    }
}
