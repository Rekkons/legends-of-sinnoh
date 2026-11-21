using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Sinnoh.Editor
{
    /// <summary>
    /// rebuild-all 5단계(03 §4-3): Node가 만든 Gen/을 들이고(들여오기 규칙 <see cref="GenImport"/>), 피규어를 번들에서 들인다(<see cref="FigureImport"/>).
    /// 메뉴 <c>Sinnoh/Assets/Import All</c>, 배치 <c>Unity.exe -batchmode -projectPath … -executeMethod Sinnoh.Editor.ContentBuild.ImportAll -quit</c>(실패하면 종료 코드 1).
    /// <list type="bullet">
    /// <item>BGM: Pt 원곡(M9 소리 ② — build.mjs bgm 묶음이 FluidSynth로 렌더한 WAV를 Gen/Audio/Bgm/pt에 둔다)을 여기서는 들이기만 한다(Vorbis 0.5·Streaming).</item>
    /// <item>글꼴 자산(TMP 등)은 만들지 않는다: UI 방식(uGUI / UI Toolkit)이 M4-3에 정해진다(03 §8 #15). 글꼴은 아틀라스 텍스처 + 문자표 JSON으로 읽는다 [가정 M4-1e].</item>
    /// </list>
    /// 결과 한 줄 <c>[ContentBuild] …</c>를 로그에 남긴다(rebuild-all이 읽는다).
    /// </summary>
    public static class ContentBuild
    {
        [MenuItem("Sinnoh/Assets/Import All")]
        public static void Menu() => Import();

        /// <summary>배치 입구(03 §4-3 5단계).</summary>
        public static void ImportAll()
        {
            bool ok = Import();
            if (Application.isBatchMode && !ok) EditorApplication.Exit(1);
        }

        public static bool Import()
        {
            var t0 = DateTime.Now;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var fig = FigureImport.ImportAll(Environment.GetCommandLineArgs().Contains("-figuresForce"));
            AssetDatabase.SaveAssets();
            int bgm = Directory.Exists(GenImport.GenRoot + "Audio/Bgm") ? Directory.GetFiles(GenImport.GenRoot + "Audio/Bgm", "*.wav", SearchOption.AllDirectories).Length : 0;
            string line = $"[ContentBuild] 피규어 들임 {fig.Imported}·건너뜀 {fig.Skipped}·오류 {fig.Errors.Count}, BGM {bgm}곡 ({(DateTime.Now - t0).TotalSeconds:0.0}초)";
            if (fig.Ok) Debug.Log(line); else Debug.LogError(line);
            return fig.Ok;
        }
    }
}
