using System;
using Sinnoh.Data;
using UnityEngine;

namespace Sinnoh.Client
{
    // 새 판 알림(결정 148 ① — 자동 업데이트 1단계): ScreenBoot가 판 정보 파일(ReleaseFeed.Url)을 한 번 읽어 넘기고, 서명이 맞고 지금보다 새 판이면
    // 타이틀 아래 오른쪽에 「새 버전 x.y.z — [받기]」(받기 = GitHub Releases 페이지 — 결정 150). 실패(오프라인 · 서명 틀림 · 같은 판)는 조용히 — 로그 한 줄만.
    // 자동 업데이트로 받을 수 있으면 [받기] 대신 [업데이트](결정 161 — GameFlow.Update.cs).
    // 웹 손님판은 하지 않는다(방장이 웹 손님판을 내준다).
    public sealed partial class GameFlow
    {
        /// <summary>알릴 새 판 번호(<see cref="TakeRelease"/> · <see cref="ShowRelease"/>가 넣는다). null이면 알림 없음.</summary>
        public string NewVersion { get; private set; }

        /// <summary>[받기]가 주소를 여는 곳(기본 <see cref="Application.OpenURL"/> — 시험 · 캡처가 바꾼다).</summary>
        public Action<string> OpenUrl { get; set; } = Application.OpenURL;

        bool WebGuest => web ?? Application.platform == RuntimePlatform.WebGLPlayer;

        /// <summary>읽어 온 latest.json 글을 확인한다: 서명이 맞고 이 빌드(<see cref="Application.version"/>)보다 새 판이면 알린다. 아니면 로그 한 줄로 끝.</summary>
        public void TakeRelease(string latestJson)
        {
            var m = ReleaseFeed.Read(latestJson, FeedKey, out string why);
            if (m != null && !ReleaseVersion.Newer(m.Version, Application.version))
            {
                why = "same";
                m = null;
            }
            if (m == null)
            {
                Debug.Log("[Release] 새 판 알림 없음 — " + why);
                return;
            }
            Debug.Log("[Release] 새 판 " + m.Version + " (지금 " + Application.version + ")");
            if (WebGuest) return;
            if (!updateView.Busy) TakeManifest(m);
            ShowRelease(m.Version);
        }

        /// <summary>새 판 알림을 띄운다(확인을 마친 판 번호). 타이틀이 열려 있으면 바로, 아니면 다음에 타이틀을 열 때. 웹 손님판은 띄우지 않는다.</summary>
        public void ShowRelease(string version)
        {
            if (string.IsNullOrEmpty(version) || WebGuest) return;
            if (Updating == UpdateStep.Idle || Updating == UpdateStep.Failed) NewVersion = version;
            SyncUpdate();
        }

        /// <summary>[받기]: GitHub Releases 페이지를 연다.</summary>
        public void DownloadRelease() => OpenUrl(ReleaseFeed.Page);
    }
}
