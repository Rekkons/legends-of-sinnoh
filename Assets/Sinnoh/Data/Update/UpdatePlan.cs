using System;
using System.Collections.Generic;

namespace Sinnoh.Data
{
    /// <summary>
    /// 받기 계획(결정 161 4-1 — 순수 함수): 서명을 확인한 새 manifest와 설치 견주기(<see cref="InstallScan"/>)로 바꿀 파일 · 받을 지문 · 지울 파일을 정한다.
    /// 받을 지문은 중복 없이(같은 내용 여러 경로는 한 번), 이미 <c>stage/</c>에 맞는 것은 빼고. 받을 지문이 <see cref="ZipOver"/>를 넘으면 묶음 zip 하나를 받아 푼다(2-5 요청 수 예산).
    /// 거부(<see cref="Why"/>)면 아무것도 하지 않는다 — 화면은 [받기]로.
    /// </summary>
    public sealed class UpdatePlan
    {
        /// <summary>받을 지문이 이것보다 많으면 zip 하나로(요청 수 예산 — 2-5).</summary>
        public const int ZipOver = 60;

        /// <summary>업데이트 도우미(설치 폴더 맨 위 — K1) · 게임 실행 파일.</summary>
        public const string Helper = "SinnohUpdater.exe", Exe = "Sinnoh.exe";

        /// <summary>디스크 여유에 더 남길 몫(4-1).</summary>
        public const long Margin = 64L << 20;

        /// <summary>거부 까닭(null = 할 수 있음): manual · store · old · url · helper · exe · path · sha · dup.</summary>
        public string Why;

        public string Version, From;

        /// <summary>바꿔 넣을 파일(경로 · 크기 · 지문 — plan.json put).</summary>
        public readonly List<ReleaseFile> Put = new List<ReleaseFile>();

        /// <summary>받을 지문(중복 없음 — Path는 그 지문을 쓰는 첫 경로).</summary>
        public readonly List<ReleaseFile> Fetch = new List<ReleaseFile>();

        /// <summary>지울 파일(지울 자리 아래 · 새 manifest에 없는 것 — plan.json delete).</summary>
        public readonly List<string> Delete = new List<string>();

        /// <summary>받을 지문 크기의 합(stage에 놓일 바이트).</summary>
        public long Bytes;

        /// <summary>zip 하나로 받나 · 그 zip(manifest packages의 .zip — Base + 이름).</summary>
        public bool UseZip;
        public ReleaseFile Zip;

        /// <summary>내려받을 바이트(zip 모드면 zip 크기).</summary>
        public long Download => UseZip ? Zip.Size : Bytes;

        /// <summary>필요한 디스크 여유(zip 모드면 zip + 푼 크기) + 여유 몫.</summary>
        public long Disk => Bytes + (UseZip ? Zip.Size : 0) + Margin;

        /// <summary>새 manifest를 자동 업데이트로 받을 수 있나(견주기 전에 — 알림이 [업데이트]냐 [받기]냐). null = 됨, 아니면 까닭.</summary>
        /// <param name="loopback">끝에서 끝 확인 프로브: http://127.0.0.1 · localhost 주소를 받아들인다(결정 161 4-4).</param>
        public static string Refuse(ReleaseManifest m, string local, bool loopback = false)
        {
            if (m == null) return "feed";
            if (!m.Auto) return "manual";
            if (string.IsNullOrEmpty(m.Store)) return "store";
            if (!ReleaseVersion.Newer(m.Version, local)) return "old";
            if (!Url(m.Store, loopback) || !m.Store.EndsWith("/", StringComparison.Ordinal) || (m.Base.Length > 0 && !Url(m.Base, loopback))) return "url";
            bool helper = false, exe = false;
            foreach (var f in m.Files)
            {
                if (!InstallPath.Safe(f.Path, out _)) return "path";
                if (!InstallScan.IsSha(f.Sha256) || f.Size < 0) return "sha";
                if (f.Path == Helper) helper = true;
                if (f.Path == Exe) exe = true;
            }
            if (!helper) return "helper";
            if (!exe) return "exe";
            if (InstallPath.Duplicate(Paths(m.Files)) != null) return "dup";
            return null;
        }

        /// <summary>계획을 만든다(순수). local = 견주기의 파일, disk = 지울 자리 아래 디스크 파일, staged = 이미 stage/에 맞게 받아 둔 지문.</summary>
        public static UpdatePlan Make(ReleaseManifest m, string local, IReadOnlyList<LocalFile> files, IReadOnlyList<string> disk, ICollection<string> staged, bool loopback = false)
        {
            var plan = new UpdatePlan { Version = m?.Version, From = local, Why = Refuse(m, local, loopback) };
            if (plan.Why != null) return plan;
            var have = new Dictionary<string, LocalFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files) have[f.Path] = f;
            var fetched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in m.Files)
            {
                if (have.TryGetValue(f.Path, out var l) && l.Size == f.Size && l.Sha256 == f.Sha256) continue;
                plan.Put.Add(f);
                if ((staged != null && staged.Contains(f.Sha256)) || !fetched.Add(f.Sha256)) continue;
                plan.Fetch.Add(f);
                plan.Bytes += f.Size;
            }
            var keep = new HashSet<string>(Paths(m.Files), StringComparer.OrdinalIgnoreCase);
            foreach (var d in disk)
                if (!keep.Contains(d) && InstallPath.Deletable(d)) plan.Delete.Add(d);
            if (plan.Fetch.Count > ZipOver && m.Base.Length > 0)
                foreach (var p in m.Packages)
                    if (p.Path != null && p.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && InstallScan.IsSha(p.Sha256) && InstallPath.Safe(p.Path, out _) && p.Path.IndexOf('/') < 0)
                    {
                        plan.UseZip = true;
                        plan.Zip = p;
                        break;
                    }
            return plan;
        }

        /// <summary>받을 주소: 파일 객체는 Store + sha256(서버가 준 경로를 쓰지 않는다 — K2), zip은 Base + 이름.</summary>
        public static string UrlOf(ReleaseManifest m, ReleaseFile f, bool zip) => zip ? m.Base + f.Path : m.Store + f.Sha256;

        static bool Url(string s, bool loopback) =>
            s.StartsWith("https://", StringComparison.Ordinal) && s.Length > 8
            || loopback && (s.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) || s.StartsWith("http://localhost:", StringComparison.Ordinal));

        static IEnumerable<string> Paths(List<ReleaseFile> files)
        {
            foreach (var f in files) yield return f.Path;
        }
    }
}
