using System;
using System.Threading.Tasks;
using Sinnoh.Data;
using Unity.Services.CloudSave;

namespace Sinnoh.Net
{
    /// <summary>플레이 기록 묶음을 올리는 창구(02 §12). 실패는 예외로 — 부르는 쪽(Client PlayUploads)이 삼킨다. 시험은 가짜를 넣는다.</summary>
    public interface IPlayUploader
    {
        /// <summary>그 이름으로 올린다(같은 이름은 덮어쓴다).</summary>
        Task UploadAsync(string name, byte[] bytes);
        /// <summary>내가 올린 기록을 서버에서 모두 지운다. 지운 수.</summary>
        Task<int> DeleteAllAsync();
    }

    /// <summary>
    /// UGS Cloud Save 「Player Files」에 올린다: 익명 로그인(<see cref="UgsBoot"/> — 솔로는 여기서 처음 로그인한다) → 파일 저장.
    /// 플레이어당 파일 200개 한도에 닿으면 가장 오래된 기록 하나를 먼저 지운다. 에디터에서는 만들지 않는다(조립 지점 ScreenBoot).
    /// </summary>
    public sealed class UgsPlayUploader : IPlayUploader
    {
        /// <summary>Cloud Save Player Files의 플레이어당 파일 수 한도(Unity 문서 2026-11-19).</summary>
        public const int MaxFiles = 200;

        public async Task UploadAsync(string name, byte[] bytes)
        {
            await UgsBoot.SignInAsync();
            var files = CloudSaveService.Instance.Files.Player;
            var list = await files.ListAllAsync();
            if (list.Count >= MaxFiles && !list.Exists(f => f.Key == name))
            {
                list.Sort((a, b) => Nullable.Compare(a.Modified, b.Modified));
                var oldest = list.Find(f => PlayRecord.Ours(f.Key));
                if (oldest != null) await files.DeleteAsync(oldest.Key);
            }
            await files.SaveAsync(name, bytes);
        }

        public async Task<int> DeleteAllAsync()
        {
            await UgsBoot.SignInAsync();
            var files = CloudSaveService.Instance.Files.Player;
            int n = 0;
            foreach (var f in await files.ListAllAsync())
            {
                if (!PlayRecord.Ours(f.Key)) continue;   // 플레이 기록 · 버그 신고 모두
                await files.DeleteAsync(f.Key);
                n++;
            }
            return n;
        }
    }
}
