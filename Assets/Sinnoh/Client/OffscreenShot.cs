using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sinnoh.Client
{
    /// <summary>
    /// 창 없이 카메라 한 장을 RenderTexture로 그려 읽는다(d3-3 §4-1, d3-1 §5-3 ⑨): URP SingleCameraRequest → rt → tex(ReadPixels).
    /// 에디터 배치 캡처(Sinnoh.Editor.BoardShots)·PlayMode 시험·Win64 측정(M4-2e)이 같이 쓴다.
    /// </summary>
    public static class OffscreenShot
    {
        public static void Render(Camera cam, RenderTexture rt, Texture2D tex)
        {
            var prevTarget = cam.targetTexture;
            cam.targetTexture = rt;
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = prevTarget;
        }

        /// <summary>분홍 픽셀(r &gt; 200, g &lt; 60, b &gt; 200 — d3-1·M4-1e 정의) 수와 배경색에서 벗어난 픽셀 비율(채널 차 합 &gt; 12).</summary>
        public static void Stats(Texture2D tex, Color32 background, out int pink, out float coverage)
        {
            var px = tex.GetPixels32();
            int fg = 0;
            pink = 0;
            foreach (var p in px)
            {
                if (Mathf.Abs(p.r - background.r) + Mathf.Abs(p.g - background.g) + Mathf.Abs(p.b - background.b) > 12) fg++;
                if (p.r > 200 && p.g < 60 && p.b > 200) pink++;
            }
            coverage = px.Length == 0 ? 0f : (float)fg / px.Length;
        }
    }
}
