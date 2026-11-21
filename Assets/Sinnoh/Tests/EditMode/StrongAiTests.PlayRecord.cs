using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using Sinnoh.Ai;
using Sinnoh.Data;
using Sinnoh.Rules;
using Sinnoh.Rules.Session;

namespace Sinnoh.Tests.EditMode
{
    /// <summary>
    /// M9 「플레이 기록 올리기 뼈대」(02 §12): 저장 폴더 → 올릴 묶음(<see cref="PlayRecord"/>). 저장은 시험이 임시 폴더에 쓴 봇 판이다 — 사용자 저장 폴더는 읽지도 쓰지도 않는다.
    /// </summary>
    public partial class StrongAiTests
    {
        /// <summary>봇 판 저장에 방 몫(닉네임 · 토큰 · 금지 토큰)과 저장 시각을 넣은 머리로 바꿔 쓴다.</summary>
        static SaveHead WithRoom(string dir)
        {
            var head = SaveStore.Deserialize<SaveHead>(File.ReadAllText(Path.Combine(dir, SaveStore.HeadFile)));
            head.SaveId = "room-20261119-203000";
            head.Summary.SavedAtMs = 1795000000000;
            head.Room = new SaveRoom { Banned = { "tok-banned-777" } };
            head.Room.Seats.Add(new SaveRoomSeat { Token = "tok-secret-111", Nickname = "한지우" });
            head.Room.Seats.Add(new SaveRoomSeat { Token = "tok-secret-222", Nickname = "Nick-Zeta9", Platform = Platform.Web, Kicked = true });
            new SaveStore(dir).WriteHead(head);
            return head;
        }

        static string Gunzip(byte[] bytes)
        {
            using (var gz = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
            using (var reader = new StreamReader(gz, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        [Test]
        public void M9_플레이기록_묶음은사람을가리키는칸을지우고_사람기록이그대로읽는다()
        {
            // 봇 판 저장(방 몫을 넣은 머리) → 묶음: 닉네임 · 토큰 · 금지 토큰 · SaveId · 저장 시각이 글자로도 남지 않는다 · 파일 이름 규칙 · 묶음을 풀어 읽은 장면 = 저장 폴더에서 읽은 장면 · 저장 폴더는 그대로
            string root = HumanTemp(), got = HumanTemp();
            try
            {
                string dir = Path.Combine(root, "bot-0");
                WriteBotSave(dir, 0);
                var head = WithRoom(dir);
                string before = FolderPrint(root);
                var pack = PlayRecord.Pack(dir, "0.9.1-rc");
                Assert.AreEqual(before, FolderPrint(root), "묶기는 읽기만 한다");
                Assert.IsNull(pack.Skip);
                Assert.AreEqual("rec_0000000000000000_f" + SaveRecord.CurrentFormat + "_r" + SaveRecord.CurrentRulesVersion + "_v0-9-1-rc", pack.FileName);
                Assert.That(pack.FileName, Does.Match("^[A-Za-z0-9_-]+$"));
                long raw = new FileInfo(Path.Combine(dir, SaveStore.HeadFile)).Length + new FileInfo(Path.Combine(dir, SaveStore.LogFile)).Length;
                Report($"M9 플레이 기록 묶음(봇 판 2인 · 시드 0): 명령 {pack.Lines}줄 · 머리 + 기록 {raw} B → gzip {pack.Bytes.Length} B");
                Assert.Less(pack.Bytes.Length, raw / 5);

                string text = Gunzip(pack.Bytes);
                foreach (var gone in new[] { "tok-secret-111", "tok-secret-222", "tok-banned-777", "한지우", "Nick-Zeta9", "room-20261119", "1795000000000" })
                    StringAssert.DoesNotContain(gone, text);

                var file = PlayRecord.Unpack(pack.Bytes);
                Assert.AreEqual("0.9.1-rc", file.GameVersion);
                Assert.AreEqual(PlayRecord.GameId(head), file.Head.SaveId);
                Assert.AreEqual(0, file.Head.Summary.SavedAtMs);
                Assert.AreEqual(2, file.Head.Room.Seats.Count, "좌석 줄은 남는다(PC/웹 · 강퇴 여부)");
                Assert.IsNull(file.Head.Room.Seats[0].Token);
                Assert.IsNull(file.Head.Room.Seats[1].Nickname);
                Assert.AreEqual(Platform.Web, file.Head.Room.Seats[1].Platform);
                Assert.IsTrue(file.Head.Room.Seats[1].Kicked);
                Assert.AreEqual(0, file.Head.Room.Banned.Count);
                Assert.AreEqual(head.Seed, file.Head.Seed);
                Assert.AreEqual(pack.Lines, file.Lines.Count);

                // 걷은 쪽: 묶음 파일만 있는 폴더를 ReadHumanSaves가 읽는다 = 저장 폴더를 읽은 것과 같은 장면 · 사람 답
                File.WriteAllBytes(Path.Combine(got, pack.FileName + "__player" + PlayRecord.Extension), pack.Bytes);
                File.WriteAllBytes(Path.Combine(got, PlayRecord.Prefix + "broken" + PlayRecord.Extension), new byte[] { 1, 2, 3 });
                var fromSave = ReadHumanSaves(ctx, root, HumanContent);
                var fromPack = ReadHumanSaves(ctx, got, HumanContent);
                Assert.AreEqual(1, fromPack.Read, fromPack.ToString());
                Assert.AreEqual(1, fromPack.Skipped[nameof(SaveProblem.Broken)]);
                Assert.Greater(fromSave.Battles.Count, 1);
                Assert.AreEqual(fromSave.Battles.Count, fromPack.Battles.Count);
                Assert.AreEqual(fromSave.HumanMoves, fromPack.HumanMoves);
                for (int i = 0; i < fromSave.Battles.Count; i++)
                    Assert.AreEqual(StateHash.Of(fromSave.Battles[i].Scene), StateHash.Of(fromPack.Battles[i].Scene), $"장면 {i}");
            }
            finally
            {
                Directory.Delete(root, true);
                Directory.Delete(got, true);
            }
        }

        [Test]
        public void M9_버그신고_묶음은설명판로그기록을담고_닉네임토큰을지우고_로그와묶음상한을지킨다_결정132()
        {
            // 봇 판 저장(방 몫을 넣은 머리) + 시험용 로그 파일(상한의 두 배 · 닉네임 · 토큰 · 사용자 폴더 경로가 줄마다) → 버그 신고 묶음.
            // 읽기만 한다 · 파일 이름 규칙 · 설명 다듬기 · 로그는 끝부분(줄 단위) · 가릴 글이 글자로도 남지 않는다 · 판 기록 = 플레이 기록과 같은 지운 머리 + 줄 · 상한 · 로그 · 판 없음 · 쓰는 중인 로그
            string root = HumanTemp();
            try
            {
                string dir = Path.Combine(root, "bot-0");
                WriteBotSave(dir, 0);
                var head = WithRoom(dir);
                string log = Path.Combine(root, "Player.log");
                var sb = new StringBuilder("FIRST-LINE-MARK\n");
                for (int i = 0; sb.Length < BugReport.MaxLogBytes * 2; i++) sb.Append("[Room] seat ").Append(i).Append(" 한지우 Nick-Zeta9 tok-secret-111 C:/Users/tester/AppData/LocalLow/x line ").Append(i).Append('\n');
                sb.Append("LAST-LINE-MARK\n");
                File.WriteAllText(log, sb.ToString(), new UTF8Encoding(false));
                var secrets = new[] { "한지우", "Nick-Zeta9", "tok-secret-111", "C:/Users/tester", null, "x" };
                BugReportInfo Info(string desc) => new BugReportInfo
                {
                    Id = "ab12cd34", Description = desc, GameVersion = "0.9.1-rc", RulesVersion = SaveRecord.CurrentRulesVersion, ProtocolVersion = Protocol.Version,
                    Lang = "ko", Screen = "1280x720", Platform = "WindowsPlayer", Role = "host", SentAtMs = 1796000000000,
                };

                Assert.IsFalse(BugReport.CanSend("  \n "));
                Assert.IsFalse(BugReport.CanSend(null));
                Assert.IsTrue(BugReport.CanSend("멈춤"));

                string before = FolderPrint(root);
                var pack = BugReport.Pack(Info("  배틀에서 멈췄다\n두 줄째  "), log, dir, secrets);
                Assert.AreEqual(before, FolderPrint(root), "묶기는 읽기만 한다");
                Assert.That(pack.FileName, Is.EqualTo("bug_1796000000000_ab12cd34_v0-9-1-rc"));
                Assert.LessOrEqual(pack.Bytes.Length, BugReport.MaxPackBytes);
                Report($"M9 버그 신고 묶음(봇 판 2인 · 로그 {new FileInfo(log).Length} B): 로그 {pack.Info.LogBytes} B + 판 기록 {pack.Info.RecordLines}줄 → gzip {pack.Bytes.Length} B");

                string text = Gunzip(pack.Bytes);
                foreach (var gone in new[] { "tok-secret-111", "tok-secret-222", "tok-banned-777", "한지우", "Nick-Zeta9", "C:/Users/tester", "room-20261119", "1795000000000", "FIRST-LINE-MARK" })
                    StringAssert.DoesNotContain(gone, text);

                var file = BugReport.Unpack(pack.Bytes);
                Assert.AreEqual("배틀에서 멈췄다\n두 줄째", file.Info.Description, "앞뒤 빈칸을 뗀다");
                Assert.AreEqual(("host", "ko", "1280x720", SaveRecord.CurrentRulesVersion, Protocol.Version, "0.9.1-rc"), (file.Info.Role, file.Info.Lang, file.Info.Screen, file.Info.RulesVersion, file.Info.ProtocolVersion, file.Info.GameVersion));
                Assert.AreEqual(new FileInfo(log).Length, file.Info.LogSize);
                Assert.LessOrEqual(file.Info.LogBytes, BugReport.MaxLogBytes, "로그 상한");
                Assert.AreEqual(file.Info.LogBytes, new UTF8Encoding(false).GetByteCount(file.Log));
                StringAssert.StartsWith("[Room] seat ", file.Log, "끝부분은 줄 처음부터");
                StringAssert.EndsWith("LAST-LINE-MARK\n", file.Log);
                StringAssert.Contains(BugReport.Redacted + " " + BugReport.Redacted + " " + BugReport.Redacted + " " + BugReport.Redacted + "/AppData", file.Log);
                StringAssert.Contains("/AppData/LocalLow/x line ", file.Log, "한 글자 · null은 가리지 않는다");

                Assert.IsNull(file.Info.RecordNote);
                var rec = PlayRecord.Unpack(PlayRecord.Gzip(file.Record));
                Assert.AreEqual(SaveStore.ReadLog(dir).Count, file.Info.RecordLines);
                Assert.AreEqual(file.Info.RecordLines, rec.Lines.Count, "판 기록 = 플레이 기록 묶음과 같은 꼴(시험대가 그대로 읽는다)");
                Assert.AreEqual(head.Seed, rec.Head.Seed);
                Assert.AreEqual(PlayRecord.GameId(head), rec.Head.SaveId);
                Assert.IsNull(rec.Head.Room.Seats[0].Nickname);
                Assert.IsNull(rec.Head.Room.Seats[1].Token);

                // 묶음 상한: 로그를 반씩(최소 MinLogBytes까지) 줄여도 넘치면 판 기록을 뺀다
                var small = BugReport.Pack(Info("x"), log, dir, secrets, maxPack: 20 * 1024);
                Assert.LessOrEqual(small.Bytes.Length, 20 * 1024);
                Assert.AreEqual("too-big", small.Info.RecordNote);
                var smallFile = BugReport.Unpack(small.Bytes);
                Assert.IsNull(smallFile.Record);
                Assert.That(small.Info.LogBytes, Is.GreaterThan(0).And.LessThanOrEqualTo(BugReport.MinLogBytes));
                StringAssert.EndsWith("LAST-LINE-MARK\n", smallFile.Log);

                // 설명 글자 수 상한 · 로그 없음 · 판 없음(손님 · 타이틀)
                var none = BugReport.Unpack(BugReport.Pack(Info(new string('가', BugReport.MaxDescription + 500)), Path.Combine(root, "nope.log"), null, null).Bytes);
                Assert.AreEqual(BugReport.MaxDescription, none.Info.Description.Length);
                Assert.IsNull(none.Log);
                Assert.AreEqual((0L, 0, "none"), (none.Info.LogSize, none.Info.LogBytes, none.Info.RecordNote));
                Assert.IsNull(none.Record);

                // 엔진이 쓰는 중인 로그(쓰기 공유로 열린 파일)도 읽는다
                using (new FileStream(log, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    Assert.Greater(BugReport.Pack(Info("x"), log, null, null).Info.LogBytes, 0);
                Assert.Throws<InvalidDataException>(() => BugReport.Unpack(new byte[] { 9, 9, 9 }));
                Assert.IsTrue(PlayRecord.Ours(pack.FileName) && PlayRecord.Ours("rec_1") && !PlayRecord.Ours("other"), "200개 한도 · [내 기록 지우기]가 둘 다 본다");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void M9_플레이기록_갤럭시단배틀이없던판과이미올린판은묶지않는다()
        {
            // 갤럭시단 자리의 명령 줄이 나오기 전까지만 남긴 저장 = 묶지 않음(사람 기록으로 읽어도 배틀 0) · 올린 줄 수 표시가 있으면 다시 묶지 않고, 줄이 늘면 다시 묶는다 · 못 읽는 머리 · 다른 형식 판의 묶음
            string root = HumanTemp();
            try
            {
                string dir = Path.Combine(root, "bot-0");
                WriteBotSave(dir, 0);
                var lines = SaveStore.ReadLog(dir);
                Assert.IsTrue(PlayRecord.HadGalacticBattle(lines));
                var game = HumanRecord.Read(ctx, SaveStore.Peek(dir).Head, lines, HumanContent);
                Assert.Greater(game.Battles.Count, 0);

                int first = lines.FindIndex(l => l.Seat == SeatId.Galactic);
                string early = Path.Combine(root, "early");
                Directory.CreateDirectory(early);
                foreach (var f in Directory.GetFiles(dir)) File.Copy(f, Path.Combine(early, Path.GetFileName(f)));
                var sb = new StringBuilder();
                for (int i = 0; i < first; i++) sb.Append(SaveStore.Serialize(lines[i], false)).Append('\n');
                File.WriteAllText(Path.Combine(early, SaveStore.LogFile), sb.ToString(), new UTF8Encoding(false));
                var none = PlayRecord.Pack(early, "1");
                Assert.IsNull(none.Bytes);
                Assert.AreEqual("no-galactic", none.Skip);

                Assert.AreEqual(-1, PlayRecord.Uploaded(dir));
                var pack = PlayRecord.Pack(dir, "1");
                Assert.IsNotNull(pack.Bytes);
                PlayRecord.MarkUploaded(dir, pack.Lines - 1);
                Assert.IsNotNull(PlayRecord.Pack(dir, "1").Bytes, "올린 뒤 줄이 늘었으면 다시 묶는다(같은 이름으로 덮어쓴다)");
                PlayRecord.MarkUploaded(dir, pack.Lines);
                var again = PlayRecord.Pack(dir, "1");
                Assert.IsNull(again.Bytes);
                Assert.AreEqual("uploaded", again.Skip);
                Assert.IsTrue(SaveStore.Peek(dir).Readable, "표시 파일이 있어도 저장은 그대로 읽힌다");

                string broken = Path.Combine(root, "broken");
                Directory.CreateDirectory(broken);
                File.WriteAllText(Path.Combine(broken, SaveStore.HeadFile), "{");
                Assert.AreEqual("head:Broken", PlayRecord.Pack(broken, "1").Skip);

                // 다른 형식 판의 묶음: 풀면 머리에 형식 판만 — 사람 기록이 「Format」으로 건너뛴다
                string text = Gunzip(pack.Bytes).Replace("\"FormatVersion\":" + SaveRecord.CurrentFormat, "\"FormatVersion\":" + (SaveRecord.CurrentFormat - 1));
                byte[] old;
                using (var ms = new MemoryStream())
                {
                    using (var gz = new GZipStream(ms, CompressionMode.Compress))
                    {
                        var raw = new UTF8Encoding(false).GetBytes(text);
                        gz.Write(raw, 0, raw.Length);
                    }
                    old = ms.ToArray();
                }
                var file = PlayRecord.Unpack(old);
                Assert.AreEqual(HumanSkip.Format, HumanRecord.Read(ctx, file.Head, file.Lines).Skip);
                Assert.Throws<InvalidDataException>(() => PlayRecord.Unpack(new byte[] { 9, 9, 9 }));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
