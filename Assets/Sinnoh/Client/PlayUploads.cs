using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sinnoh.Data;
using Sinnoh.Net;
using UnityEngine;

namespace Sinnoh.Client
{
    /// <summary>
    /// 플레이 기록 올리기(02 §12): 판이 끝났을 때 · 판을 떠날 때 흐름(<see cref="GameFlow"/>)이 그 판의 저장 폴더를 <see cref="Offer"/>에 넘긴다.
    /// 설정(<see cref="SoundPrefs.PlayUpload"/> — 기본 켜짐, 첫 실행 알림 전에는 꺼진 것과 같다)이 꺼져 있으면 아무것도 하지 않는다 — 묶지도, 로그인하지도 않는다.
    /// 켜져 있으면 뒤에서 묶고(<see cref="PlayRecord.Pack"/> — 작업 스레드) 올린다. 실패(오프라인 · UGS 꺼짐)는 삼키고 그 폴더를 기억했다가 다음 <see cref="Offer"/> 때 한 번 더 해 본다.
    /// 같은 판은 다시 올리지 않는다(저장 폴더의 표시 파일 <see cref="PlayRecord.MarkFile"/> — 줄이 늘었거나 되돌려 갈래가 바뀌었을 때만 같은 이름으로 덮어쓴다).
    /// </summary>
    public sealed class PlayUploads
    {
        readonly IPlayUploader uploader;
        readonly string gameVersion;
        readonly Func<bool> enabled;
        string retry;
        Task running = Task.CompletedTask;

        /// <param name="enabled">올리기 설정이 켜져 있나(부를 때마다 묻는다).</param>
        public PlayUploads(IPlayUploader uploader, string gameVersion, Func<bool> enabled)
        {
            this.uploader = uploader ?? throw new ArgumentNullException(nameof(uploader));
            this.gameVersion = gameVersion;
            this.enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        }

        /// <summary>
        /// 서버를 부르지 않는 창구(확인용 플레이어 · 캡처 — 설정 줄 · 지우기 · 버그 신고 창만 지난다): 올리기는 아무 일도 없고(<paramref name="upload"/>를 주면 그것 — 캡처의 실패 · 보내는 중)
        /// 지우기는 <paramref name="deletes"/>개를 지웠다고 답한다.
        /// </summary>
        public static PlayUploads Quiet(Func<bool> enabled, int deletes = 0, Func<string, byte[], Task> upload = null) => new PlayUploads(new QuietUploader(deletes, upload), "0.0.quiet", enabled);

        sealed class QuietUploader : IPlayUploader
        {
            readonly int deletes;
            readonly Func<string, byte[], Task> upload;
            public QuietUploader(int deletes, Func<string, byte[], Task> upload)
            {
                this.deletes = deletes;
                this.upload = upload;
            }
            public Task UploadAsync(string name, byte[] bytes) => upload != null ? upload(name, bytes) : Task.CompletedTask;
            public Task<int> DeleteAllAsync() => Task.FromResult(deletes);
        }

        /// <summary>밀린 올리기가 모두 끝나는 Task(시험이 기다린다). 실패로 끝나지 않는다.</summary>
        public Task Idle => running;

        /// <summary>지금 올리나(설정 켬 + 첫 실행 알림을 봄 — 방장이 방을 열 때 로비 알림 줄 값으로도 쓴다).</summary>
        public bool Enabled => enabled();

        /// <summary>설정으로 본 올리기 규칙(결정 126 ④): 첫 실행 알림을 봤고(<see cref="SoundPrefs.PlayNotice"/>) 「플레이 기록 보내기」가 켜져 있다 — 알림 전에는 올리지 않는다 [가정 plan §5 #650].</summary>
        public static bool Allowed(SoundPrefs prefs) => prefs != null && prefs.PlayNotice && prefs.PlayUpload;

        /// <summary>그 저장 폴더의 판을 올려 본다(꺼져 있거나 폴더가 없으면 아무것도 하지 않는다). 기다리지 않는다 — 게임을 막지 않는다.</summary>
        public void Offer(string saveDir)
        {
            if (string.IsNullOrEmpty(saveDir) || !enabled()) return;
            var dirs = new List<string>();
            if (retry != null && retry != saveDir) dirs.Add(retry);
            dirs.Add(saveDir);
            retry = null;
            running = Run(running, dirs, saveDir);
        }

        /// <summary>내가 올린 기록(플레이 기록 · 버그 신고)을 서버에서 모두 지운다(설정 화면 [내 기록 지우기] — <see cref="SettingsScreen.DeleteRecords"/>). 지운 수, 실패하면 −1.</summary>
        public async Task<int> DeleteMine()
        {
            try
            {
                return await uploader.DeleteAllAsync();
            }
            catch (Exception e)
            {
                Debug.Log("[PlayUploads] 지우기 실패: " + e.Message);
                return -1;
            }
        }

        /// <summary>
        /// 버그 신고 묶음을 올린다(M9 — 결정 132 ①, <see cref="BugReport"/>): 사람이 [보내기]를 누른 것이라 플레이 기록 설정(<see cref="Enabled"/>)과 상관없이 보낸다 [가정 plan §5 #680].
        /// 보냈으면 true, 실패(오프라인 · UGS 꺼짐)면 false — 창이 「다시 보내기」를 보인다.
        /// </summary>
        public async Task<bool> SendBug(string name, byte[] bytes)
        {
            try
            {
                await uploader.UploadAsync(name, bytes);
                return true;
            }
            catch (Exception e)
            {
                Debug.Log("[PlayUploads] 버그 신고를 보내지 못함: " + e.Message);
                return false;
            }
        }

        async Task Run(Task before, List<string> dirs, string fresh)
        {
            await before;
            foreach (var dir in dirs)
            {
                try
                {
                    var pack = await Task.Run(() => PlayRecord.Pack(dir, gameVersion));
                    if (pack.Bytes == null) continue;
                    await uploader.UploadAsync(pack.FileName, pack.Bytes);
                    PlayRecord.MarkUploaded(dir, pack.Lines, pack.Fingerprint); // 그새 로그가 잘렸으면(되돌림) 지문이 달라 다음 Offer가 다시 올린다
                }
                catch (Exception e)
                {
                    // 조용히 건너뛴다(경고 · 오류 로그도 내지 않는다). 이번에 받은 폴더만 다음에 한 번 더 — 다시 해 본 것이 또 실패하면 버린다
                    if (dir == fresh) retry = dir;
                    Debug.Log("[PlayUploads] 올리지 못함: " + e.Message);
                }
            }
        }
    }
}
