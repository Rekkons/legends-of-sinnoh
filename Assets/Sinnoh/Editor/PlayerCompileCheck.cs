using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Player;
using UnityEngine;

namespace Sinnoh.Editor
{
    // 플레이어 빌드 컴파일 확인(isekai PlayerCompileCheck에서 옮김): 에디터 시험은 UNITY_EDITOR 밖에서 에디터 전용 형식을 부르는 것 같은
    // 「빌드에서만 나는」 컴파일 에러를 못 잡는다. 에디터를 띄운 채 Win64 · WebGL 플레이어 스크립트만 컴파일한다(빌드·도메인 리로드 없음 — 관문 full의 player 단계).
    // WebGL(11-20 더함 — 웹 손님판의 #if UNITY_WEBGL 갈래 · 패키지(Cloud Save 등)): 활성 대상이 아닌 WebGL 컴파일은 UnityEngine.WebGLModule을 참조하지 않아
    // WebGLInput이 거짓 에러다(isekai 11-04 · sinnoh 11-20 확인) — 그 두 줄만 SINNOH_COMPILE_CHECK로 비켜 간다(WebTextInput). 웹 모듈 형식을 새로 쓰면 같은 식으로 비켜 간다.
    // 결과 Temp/player-compile.json {"ok":bool,"targets":[{"target","ok","assemblies","errors":[…]}]}
    public static class PlayerCompileCheck
    {
        public const string ResultPath = "Temp/player-compile.json";
        const string OutRoot = "Temp/PlayerCompileCheck";

        [MenuItem("Sinnoh/Build/Check Player Compile")]
        public static void Run()
        {
            var sb = new StringBuilder("{\"targets\":[");
            bool win = One(sb, BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, first: true, defines: null);
            bool web = One(sb, BuildTarget.WebGL, BuildTargetGroup.WebGL, first: false, defines: new[] { "SINNOH_COMPILE_CHECK" });
            bool all = win && web;
            sb.Append("],\"ok\":").Append(all ? "true" : "false").Append('}');
            File.WriteAllText(ResultPath, sb.ToString(), new UTF8Encoding(false));
            if (all) Debug.Log("[PlayerCompileCheck] Win64 · WebGL 플레이어 스크립트 컴파일 통과 → " + ResultPath);
            else Debug.LogError("[PlayerCompileCheck] 플레이어 스크립트 컴파일 실패(Win64 " + (win ? "통과" : "실패") + " · WebGL " + (web ? "통과" : "실패") + ") → " + ResultPath);
        }

        static bool One(StringBuilder sb, BuildTarget target, BuildTargetGroup group, bool first, string[] defines)
        {
            var errors = new List<string>();
            var seen = new HashSet<string>();
            // 컴파일 에러는 결과 객체에 없고 콘솔 로그로만 나온다(다른 스레드에서 올 수 있어 잠근다)
            void Grab(string msg, string stack, LogType type)
            {
                if (msg == null || !msg.Contains("error CS")) return;
                lock (errors) if (seen.Add(msg)) errors.Add(msg.Trim());
            }
            string dir = Path.Combine(OutRoot, target.ToString());
            Directory.CreateDirectory(dir);
            int assemblies = 0;
            Application.logMessageReceivedThreaded += Grab;
            try
            {
#pragma warning disable 618
                var settings = new ScriptCompilationSettings { target = target, group = group, options = ScriptCompilationOptions.None, extraScriptingDefines = defines };
#pragma warning restore 618
                var result = PlayerBuildInterface.CompilePlayerScripts(settings, dir);
                assemblies = result.assemblies?.Count ?? 0;
            }
            catch (System.Exception e) { errors.Add("예외: " + e.Message); }
            finally { Application.logMessageReceivedThreaded -= Grab; }
            bool ok = errors.Count == 0 && assemblies > 0;
            if (!ok && errors.Count == 0) errors.Add("컴파일된 어셈블리 0개(콘솔 확인)");
            if (!first) sb.Append(',');
            sb.Append("{\"target\":\"").Append(target).Append("\",\"ok\":").Append(ok ? "true" : "false")
              .Append(",\"assemblies\":").Append(assemblies).Append(",\"errors\":[");
            for (int i = 0; i < errors.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Esc(errors[i])).Append('"');
            }
            sb.Append("]}");
            return ok;
        }

        static string Esc(string s)
        {
            var b = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c == '\n') b.Append("\\n");
                else if (c == '\r') { }
                else if (c < ' ') b.Append(' ');
                else b.Append(c);
            }
            return b.ToString();
        }
    }
}
