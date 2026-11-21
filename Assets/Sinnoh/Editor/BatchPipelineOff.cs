using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Sinnoh.Editor
{
    /// <summary>
    /// 배치 Unity(관문·캡처·빌드)에서만 com.unity.pipeline 파이프라인 서버를 끈다(사용자 결정 86 — 결정 85 ②의 막는 길을 바꿈).
    /// 패키지의 [InitializeOnLoad] PipelineServerStartup이 배치 모드에서도 서버를 띄워, 떠 있는 <c>unity mcp</c> 프록시가 관문의 배치 Unity에 붙어
    /// 「editor status」 명령을 보내 오류 로그 → 시험 실패를 냈다. Unity MCP는 켠 채로 두고(대화형 에디터는 아무것도 하지 않는다) 배치에서만 끈다.
    /// 어셈블리 사이 InitializeOnLoad 순서가 정해져 있지 않아 지금 한 번 + delayCall 한 번(도메인 리로드 뒤에도 생성자가 다시 돈다). 패키지가 없으면 조용히 넘어간다.
    /// </summary>
    [InitializeOnLoad]
    static class BatchPipelineOff
    {
        internal const string StartupType = "Unity.Pipeline.Editor.PipelineServerStartup, Unity.Pipeline.Editor";

        static BatchPipelineOff()
        {
            if (!Application.isBatchMode) return;
            Stop();
            EditorApplication.delayCall += Stop;
        }

        static void Stop()
        {
            var stop = Type.GetType(StartupType)?.GetMethod("StopServer", BindingFlags.Public | BindingFlags.Static);
            if (stop == null) return;
            bool was = Running() == true;
            stop.Invoke(null, null);
            if (was) Debug.Log("[BatchPipelineOff] 배치 모드: 파이프라인 서버를 껐다(Unity MCP가 이 Unity에 붙지 않게 — 결정 86)");
        }

        /// <summary>파이프라인 서버가 돌고 있나(패키지가 없으면 null) — EditorAssets 시험이 본다.</summary>
        internal static bool? Running()
        {
            var t = Type.GetType(StartupType);
            if (t == null) return null;
            var server = t.GetProperty("Server", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (server == null) return false;
            return server.GetType().GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(server) as bool? ?? false;
        }
    }
}
