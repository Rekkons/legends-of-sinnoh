using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sinnoh.Data;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 자동 업데이트 끝에서 끝 확인용 프로브(결정 161 4-4 — 사람 실행에는 영향 없음): <c>-sinnohUpdateProbe &lt;폴더&gt;</c>가 있으면 사람 플레이어처럼 판 정보를 읽되,
    /// 알림이 [업데이트]면 스스로 누르고, 단계마다 <c>&lt;폴더&gt;/update-probe.json</c>(판 · 단계 · 성공 · 까닭 · 시간)을 다시 쓴다. 켤 때 도우미 결과(result.json)가 있으면 그것을 적고 끝낸다.
    /// <c>-sinnohSaves &lt;임시&gt;</c> 없이는 돌지 않는다(사용자 저장 폴더 보호). <c>-sinnohFeed &lt;주소&gt;</c>는 이 프로브가 있고 주소가 <c>http://127.0.0.1:</c> · <c>http://localhost:</c>일 때만 받아들이고,
    /// 그때 서명 확인 키는 RFC 8032 시험 1 공개 키(<c>pack.mjs keyFromSeed</c>와 같은 것)다 — 실제 키로 시험 견본을 만들지 않는다(RULE-RELEASE).
    /// </summary>
    public sealed class UpdateProbe
    {
        public const string Arg = "-sinnohUpdateProbe", FeedArg = "-sinnohFeed", FileName = "update-probe.json";

        /// <summary>RFC 8032 §7.1 시험 1 공개 키.</summary>
        public const string TestKeyHex = "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a";

        public static bool On => Value(Arg) != null;

        /// <summary>받아들인 판 정보 주소(프로브 + loopback일 때만) 또는 null.</summary>
        public static string Feed()
        {
            string url = Value(FeedArg);
            if (!On || url == null) return null;
            return url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) || url.StartsWith("http://localhost:", StringComparison.Ordinal) ? url : null;
        }

        public static byte[] TestKey()
        {
            var b = new byte[32];
            for (int i = 0; i < 32; i++) b[i] = Convert.ToByte(TestKeyHex.Substring(i * 2, 2), 16);
            return b;
        }

        static string Value(string arg)
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, arg);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }

        readonly string file;
        readonly List<string> lines = new List<string>();

        UpdateProbe(string folder)
        {
            Directory.CreateDirectory(folder);
            file = Path.Combine(folder, FileName);
            if (File.Exists(file))
            {
                // 옛 판 → 새 판이 같은 파일에 이어 적는다(도우미가 같은 인자로 다시 켠다)
                string old = File.ReadAllText(file, Encoding.UTF8);
                int a = old.IndexOf('['), b = old.LastIndexOf(']');
                if (a >= 0 && b > a)
                    foreach (var l in old.Substring(a + 1, b - a - 1).Split('\n'))
                    {
                        string t = l.Trim().TrimEnd(',');
                        if (t.StartsWith("{", StringComparison.Ordinal)) lines.Add(t);
                    }
            }
        }

        /// <summary>프로브가 켜져 있으면 만든다(저장 폴더 인자가 없으면 null + 로그 — 돌지 않는다).</summary>
        public static UpdateProbe Open(out bool refused)
        {
            refused = false;
            string folder = Value(Arg);
            if (folder == null) return null;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), ScreenBoot.SavesArg) < 0)
            {
                Debug.LogWarning("[UpdateProbe] " + ScreenBoot.SavesArg + " <임시 폴더> 없이는 돌지 않는다(사용자 저장 폴더 보호)");
                refused = true;
                return null;
            }
            return new UpdateProbe(folder);
        }

        /// <summary>한 줄을 더하고 파일을 다시 쓴다.</summary>
        public void Note(string step, bool ok, string why = null)
        {
            lines.Add("{\"version\":" + Str(Application.version) + ",\"step\":" + Str(step) + ",\"ok\":" + (ok ? "true" : "false") + ",\"why\":" + Str(why ?? "") +
                ",\"ms\":" + ((long)(Time.realtimeSinceStartup * 1000)).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"pid\":" + System.Diagnostics.Process.GetCurrentProcess().Id + "}");
            File.WriteAllText(file, "{\"format\":1,\"steps\":[\n" + string.Join(",\n", lines) + "\n]}\n", new UTF8Encoding(false));
        }

        /// <summary>저장 폴더(<c>-sinnohSaves</c>)의 판을 읽어 적는다 — 업데이트 뒤에도 열리나(읽을 수 있는 수 / 전체).</summary>
        public void Saves(GameFlow flow)
        {
            var saves = flow.Saves();
            int readable = saves.FindAll(e => e.Readable).Count;
            Note("saves", readable == saves.Count, readable + "/" + saves.Count);
        }

        /// <summary>흐름의 단계마다 적고, 알림이 [업데이트]면 누른다 · 끝(실패 · 다시 시작 없음)이면 게임을 끈다.</summary>
        public void Attach(GameFlow flow)
        {
            flow.UpdateChanged += (step, why) =>
            {
                Note(step.ToString().ToLowerInvariant(), step != UpdateStep.Failed, why);
                if (step == UpdateStep.Failed || (step == UpdateStep.Idle && why == "cancel")) Application.Quit();
            };
        }

        /// <summary>판 정보를 받은 뒤: [업데이트]면 누르고, 아니면 까닭을 적고 끝낸다.</summary>
        public void Released(GameFlow flow, string why)
        {
            Saves(flow);
            if (why != null)
            {
                Note("feed", false, why);
                Application.Quit();
                return;
            }
            Note("feed", true, flow.NewVersion);
            if (!flow.AutoUpdate || !(flow.Stage.Current is TitleScreen title) || !title.PressUpdate())
            {
                Note("notice", false, flow.AutoUpdate ? "press" : "manual");
                Application.Quit();
            }
        }

        static string Str(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
