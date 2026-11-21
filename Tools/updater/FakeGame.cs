// 도우미 자기 시험용 가짜 게임(build.mjs --self-test가 임시 폴더에 컴파일해 Sinnoh.exe로 둔다 — 배포하지 않는다).
//   켜지면 환경 변수 FAKEGAME_LOG 파일에 받은 인자를 JSON 배열 한 줄로 덧붙인다(--hold면 파일을 쥔 뒤에).
//   --sleep <ms>  그만큼 버티다 끝난다(없으면 바로 끝남)
//   --hold <파일> 그 파일을 독점(FileShare.None)으로 쥐고 있다가 끝날 때 놓는다
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

static class FakeGame
{
    static int Main(string[] args)
    {
        int sleep = 0;
        string hold = null;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--sleep") int.TryParse(args[i + 1], out sleep);
            if (args[i] == "--hold") hold = args[i + 1];
        }
        var held = hold != null ? new FileStream(hold, FileMode.Open, FileAccess.Read, FileShare.None) : null;
        string log = Environment.GetEnvironmentVariable("FAKEGAME_LOG");
        if (!string.IsNullOrEmpty(log)) File.AppendAllText(log, new JavaScriptSerializer().Serialize(args) + "\n", new UTF8Encoding(false));
        Thread.Sleep(sleep);
        held?.Dispose();
        return 0;
    }
}
