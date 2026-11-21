using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Sinnoh.Tests.EditorAssets
{
    /// <summary>
    /// 사용자 결정 86: 배치 Unity(관문·캡처·빌드)에서는 com.unity.pipeline 파이프라인 서버가 꺼져 있다(<c>Sinnoh.Editor.BatchPipelineOff</c>) —
    /// 떠 있는 <c>unity mcp</c> 프록시가 관문의 배치 Unity에 붙지 못한다. 대화형 에디터에서는 서버를 그대로 두므로 보지 않는다. 패키지가 없으면 건너뛴다.
    /// </summary>
    public class BatchPipelineOffTests
    {
        [Test]
        public void M6_배치Unity에서는파이프라인서버가꺼져있다()
        {
            if (!Application.isBatchMode) Assert.Ignore("대화형 에디터 — 서버를 끄지 않는다(배치에서만 본다)");
            var t = Type.GetType("Unity.Pipeline.Editor.PipelineServerStartup, Unity.Pipeline.Editor");
            if (t == null) Assert.Ignore("com.unity.pipeline 없음");
            var server = t.GetProperty("Server", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (server == null) return;   // 껐다(StopServer가 m_Server를 비운다)
            var running = server.GetType().GetProperty("IsRunning", BindingFlags.Public | BindingFlags.Instance)?.GetValue(server);
            Assert.AreEqual(false, running, "배치 Unity의 파이프라인 서버가 돌고 있다 — Unity MCP가 붙을 수 있다");
        }
    }
}
